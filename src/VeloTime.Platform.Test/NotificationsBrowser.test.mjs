import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../WebUI.Mud.Client/wwwroot/notifications.js", import.meta.url), "utf8");
const ui = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

test("BFF transport rejects non-API and cross-origin paths", async () => {
    for (const path of ["https://other.invalid/api/notifications", "//other.invalid/api/notifications", "/hubs/notifications"]) {
        await assert.rejects(ui.request("bad", path, "GET", null));
    }
});

test("scope-only mutation obtains CSRF token; no bearer or identity headers", async () => {
    const original = globalThis.fetch, calls = [];
    globalThis.fetch = async (path, options) => {
        calls.push({ path, options });
        return path === "/api/antiforgery" ? new Response('"csrf"') :
            new Response('{"notificationId":"test"}', { headers: { "X-VeloTime-Authenticated": "true", "X-VeloTime-HasSession": "true" } });
    };
    try {
        const response = await ui.request("save", "/api/notifications/notice/dismissal", "PUT", '{"scope":"session"}');
        assert.equal(response.authenticated, true);
        assert.equal(response.hasSession, true);
        assert.deepEqual(calls.map(c => c.path), ["/api/antiforgery", "/api/notifications/notice/dismissal"]);
        assert.deepEqual(calls[1].options.headers, { "X-CSRF-TOKEN": "csrf", "Content-Type": "application/json" });
        assert.equal(calls[1].options.body, '{"scope":"session"}');
        assert.equal(calls[1].options.credentials, "same-origin");
        assert.equal(calls[1].options.cache, "no-store");
    } finally { globalThis.fetch = original; }
});

test("failed CSRF acquisition prevents mutation", async () => {
    const original = globalThis.fetch, calls = [];
    globalThis.fetch = async path => { calls.push(path); return new Response("", { status: 401 }); };
    try {
        assert.equal((await ui.request("save", "/api/notifications/id/dismissal", "DELETE", null)).status, 401);
        assert.deepEqual(calls, ["/api/antiforgery"]);
    } finally { globalThis.fetch = original; }
});

test("anonymous storage is app and UUID scoped, persists reload, and reports storage failure", () => {
    const original = globalThis.sessionStorage;
    const values = new Map();
    globalThis.sessionStorage = { getItem: id => values.get(id), setItem: (id, value) => values.set(id, value) };
    try {
        ui.dismissAnonymous("a");
        assert.deepEqual(ui.anonymousDismissed(["a", "b"]), ["a"]);
        assert.equal(values.get("velotime:notifications:anonymous:v1:a"), "1");
        assert.deepEqual(ui.anonymousDismissed(["a"]), ["a"]);
        globalThis.sessionStorage = { setItem() { throw new Error("denied"); }, getItem() { throw new Error("denied"); } };
        assert.throws(() => ui.dismissAnonymous("b"), /denied/);
        assert.throws(() => ui.anonymousDismissed(["a"]), /denied/);
    } finally { globalThis.sessionStorage = original; }
});

test("BFF cancellation aborts the matching request without affecting another", async () => {
    const original = globalThis.fetch;
    const signals = new Map();
    globalThis.fetch = (path, options) => new Promise((resolve, reject) => {
        signals.set(path, { signal: options.signal, resolve });
        options.signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
    });
    try {
        const first = ui.request("first", "/api/notifications/first", "GET", null);
        const second = ui.request("second", "/api/notifications/second", "GET", null);
        ui.abort("first");
        await assert.rejects(first, { name: "AbortError" });
        assert.equal(signals.get("/api/notifications/second").signal.aborted, false);
        signals.get("/api/notifications/second").resolve(new Response("{}"));
        await second;
        ui.abort("second"); // Completed request is no longer retained.
        assert.equal(signals.get("/api/notifications/second").signal.aborted, false);
    } finally { globalThis.fetch = original; }
});

test("module is browser storage/BFF only, has no transport or logout subscription API", () => {
    assert.deepEqual(Object.keys(ui).sort(), ["abort", "anonymousDismissed", "dismissAnonymous", "request"]);
    assert.doesNotMatch(source, /WebSocket|BroadcastChannel|negotiate|invokeMethodAsync|addEventListener|confirmAnonymous/);
});
