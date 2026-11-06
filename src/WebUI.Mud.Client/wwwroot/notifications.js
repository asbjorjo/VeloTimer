// Browser-only BFF fetch and tab-local anonymous dismissals.
// SignalR is owned by the Microsoft .NET client in WASM, not JavaScript.
const requests = new Map();
export async function request(id, path, method, body) {
    if (!path.startsWith("/api/") || path.startsWith("//")) throw new Error("Invalid BFF path");
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15000);
    requests.set(id, controller);
    try {
        const headers = {};
        if (method !== "GET") {
            const token = await fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store", signal: controller.signal });
            if (!token.ok) return { status: token.status, body: await token.text() };
            headers["X-CSRF-TOKEN"] = await token.json();
        }
        if (body !== null) headers["Content-Type"] = "application/json";
        const response = await fetch(path, { method, headers, body, credentials: "same-origin", cache: "no-store", signal: controller.signal });
        return { status: response.status, body: await response.text(),
            authenticated: response.headers.get("X-VeloTime-Authenticated") === "true",
            hasSession: response.headers.get("X-VeloTime-HasSession") === "true" };
    } finally { clearTimeout(timeout); requests.delete(id); }
}
export function abort(id) { requests.get(id)?.abort(); }

const prefix = "velotime:notifications:anonymous:v1:";
export function anonymousDismissed(ids) {
    return ids.filter(id => sessionStorage.getItem(prefix + id) === "1");
}
export function dismissAnonymous(id) { sessionStorage.setItem(prefix + id, "1"); }
