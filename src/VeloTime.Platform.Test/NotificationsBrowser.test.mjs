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

test("scope-only mutation obtains CSRF token; no assertion or identity headers", async () => {
    const original = globalThis.fetch, calls = [];
    globalThis.fetch = async (path, options) => {
        calls.push({ path, options });
        return path === "/api/antiforgery" ? new Response('"csrf"') :
            new Response('{"notificationId":"test"}', { headers: { "X-VeloTime-Authenticated": "true" } });
    };
    try {
        const response = await ui.request("save", "/api/notifications/notice/dismissal", "PUT", '{"scope":"session"}');
        assert.equal(response.authenticated, true);
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
        // A fresh module/reload reads the same tab's backing store.
        assert.deepEqual(ui.anonymousDismissed(["a"]), ["a"]);
        globalThis.sessionStorage = { setItem() { throw new Error("denied"); }, getItem() { throw new Error("denied"); } };
        assert.throws(() => ui.dismissAnonymous("b"), /denied/);
        assert.throws(() => ui.anonymousDismissed(["a"]), /denied/);
    } finally { globalThis.sessionStorage = original; }
});

test("SignalR subscribes before snapshot callback, deduplicates hints and disposes only its owner", async () => {
    const originals = { fetch: globalThis.fetch, document: globalThis.document, location: globalThis.location,
        WebSocket: globalThis.WebSocket, BroadcastChannel: globalThis.BroadcastChannel };
    const listeners = new Map(), sockets = [], calls = [];
    globalThis.document = { hidden: false, addEventListener: (key, value) => listeners.set(key, value),
        removeEventListener: (key, value) => { if (listeners.get(key) === value) listeners.delete(key); } };
    globalThis.location = { href: "https://ui.invalid/profile" };
    globalThis.BroadcastChannel = undefined;
    globalThis.fetch = async () => new Response(JSON.stringify({
        connectionToken: "transport-token", availableTransports: [{ transport: "WebSockets" }]
    }));
    class Socket {
        static OPEN = 1;
        constructor(url) { this.url = url; this.readyState = 1; sockets.push(this); setTimeout(() => this.onopen(), 0); }
        send(value) { if (value.includes('"protocol"')) setTimeout(() => this.onmessage({ data: "{}\u001e" }), 0); }
        close() { this.readyState = 3; this.onclose?.(); }
        hint(id) { this.onmessage({ data: JSON.stringify({ type: 1, target: "NotificationsRefresh", arguments: [{ eventId: id }] }) + "\u001e" }); }
    }
    globalThis.WebSocket = Socket;
    const dotnet = { invokeMethodAsync: async name => { calls.push(name); } };
    try {
        assert.equal(await ui.subscribe(dotnet, "first"), true);
        assert.deepEqual(calls, ["Connected"]);
        assert.equal(new URL(sockets[0].url).protocol, "wss:");
        assert.equal(new URL(sockets[0].url).searchParams.has("access_token"), false);
        sockets[0].hint("event1"); sockets[0].hint("event1");
        sockets[0].hint("event2"); // Unique burst hints still coalesce to one refresh.
        await new Promise(resolve => setTimeout(resolve, 170));
        assert.deepEqual(calls, ["Connected", "Refresh"]);
        await ui.subscribe(dotnet, "second");
        assert.equal(sockets[0].readyState, 3);
        ui.unsubscribe("first");
        assert.equal(sockets[1].readyState, 1);
        listeners.get("visibilitychange")();
        assert.equal(calls.at(-1), "Refresh");
        ui.unsubscribe("second");
        assert.equal(sockets[1].readyState, 3);
        assert.equal(listeners.size, 0);
    } finally {
        ui.unsubscribe("second");
        Object.assign(globalThis, originals);
    }
});
