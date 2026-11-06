// First-party, bounded SignalR JSON/WebSocket transport. Same-origin cookies only.
// No bearer tokens, identity groups, CDN, or third-party browser code.
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

let active;
export async function subscribe(dotnet, owner) {
    active?.stop();
    const state = new HintConnection(dotnet);
    state.owner = owner;
    active = state;
    return await state.start();
}
export function unsubscribe(owner) {
    if (active?.owner === owner) { active.stop(); active = undefined; }
}

class HintConnection {
    constructor(dotnet) {
        this.dotnet = dotnet;
        this.stopped = false;
        this.attempt = 0;
        this.seen = new Set();
        this.resume = () => { if (!document.hidden) this.notify("Refresh"); };
        this.channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("velotime:notification-auth") : null;
        this.channel?.addEventListener("message", event => {
            if (event.data === "logout") {
                this.notify("LoggedOut");
                this.confirmAnonymous();
                this.socket?.close(); // Re-negotiate the cookie's current state.
            }
        });
        this.logout = event => {
            if (event.target instanceof HTMLFormElement &&
                new URL(event.target.action, location.href).pathname === "/authentication/logout") {
                this.notify("LoggedOut");
                this.channel?.postMessage("logout");
                this.stop();
            }
        };
        document.addEventListener("visibilitychange", this.resume);
        document.addEventListener("submit", this.logout, true);
        // Snapshot polling also detects stale auth and repairs missed hints.
        this.poll = setInterval(() => { if (!document.hidden) this.notify("Refresh"); }, 60000);
    }
    notify(method) { if (!this.stopped) this.dotnet.invokeMethodAsync(method).catch(() => this.stop()); }
    hint() {
        if (this.hintTimer || this.stopped) return;
        this.hintTimer = setTimeout(() => {
            this.hintTimer = undefined;
            this.notify("Refresh");
        }, 150);
    }
    async confirmAnonymous() {
        if (this.stopped) return;
        try {
            // Cookie-only BFF metadata. No Notifications API call or session ID.
            const response = await fetch("/api/notifications/session", {
                credentials: "same-origin", cache: "no-store", signal: AbortSignal.timeout(5000)
            });
            if (response.ok && (await response.json()).authenticated === false) { this.notify("ResumeAnonymous"); return; }
        } catch { /* Logout may still be in progress; keep private content cleared. */ }
        this.logoutProbe = setTimeout(() => this.confirmAnonymous(), 1000);
    }
    async start() {
        try { await this.connect(); return true; }
        catch { this.notify("TransportFailed"); this.retry(); return false; }
    }
    async connect() {
        this.abort = new AbortController();
        this.timeout = setTimeout(() => this.abort.abort(), 10000);
        let response;
        try {
            response = await fetch("/hubs/notifications/negotiate?negotiateVersion=1", {
                method: "POST", credentials: "same-origin", cache: "no-store", signal: this.abort.signal
            });
        } finally { clearTimeout(this.timeout); }
        if (!response.ok) throw new Error("Hub unavailable");
        const negotiation = await response.json();
        if (this.stopped) return;
        if (!negotiation.connectionToken || negotiation.url ||
            !negotiation.availableTransports?.some(t => t.transport === "WebSockets")) throw new Error("WebSockets required");
        const url = new URL("/hubs/notifications", location.href);
        url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
        url.searchParams.set("id", negotiation.connectionToken);
        await new Promise((resolve, reject) => {
            const socket = this.socket = new WebSocket(url);
            let connected = false, buffer = "";
            const timeout = setTimeout(() => { socket.close(); reject(new Error("Handshake timeout")); }, 10000);
            socket.onopen = () => socket.send('{"protocol":"json","version":1}\u001e');
            socket.onmessage = event => {
                clearTimeout(this.silence);
                this.silence = setTimeout(() => socket.close(), 45000);
                buffer += event.data;
                const messages = buffer.split("\u001e");
                buffer = messages.pop();
                for (const raw of messages) {
                    try {
                        const message = JSON.parse(raw);
                        if (!connected) {
                            if (message.error) { socket.close(); reject(new Error("Handshake failed")); return; }
                            connected = true; clearTimeout(timeout); this.attempt = 0;
                            this.ping = setInterval(() => { if (socket.readyState === WebSocket.OPEN) socket.send('{"type":6}\u001e'); }, 15000);
                            this.notify("Connected"); resolve();
                        } else if (message.type === 1 && message.target === "NotificationsRefresh") {
                            const id = message.arguments?.[0]?.eventId;
                            if (typeof id === "string" && !this.seen.has(id)) {
                                this.seen.add(id);
                                if (this.seen.size > 256) this.seen.delete(this.seen.values().next().value);
                                this.hint();
                            }
                        } else if (message.type === 7) socket.close();
                    } catch { socket.close(); }
                }
            };
            socket.onerror = () => { clearTimeout(timeout); reject(new Error("Hub unavailable")); };
            socket.onclose = () => {
                clearTimeout(timeout); clearTimeout(this.silence); clearInterval(this.ping);
                if (!connected) reject(new Error("Hub unavailable"));
                if (connected && !this.stopped) { this.notify("TransportFailed"); this.retry(); }
            };
        });
    }
    retry() {
        if (this.stopped) return;
        clearTimeout(this.retryTimer);
        this.retryTimer = setTimeout(() => this.start(), Math.min(30000, 1000 * 2 ** Math.min(this.attempt++, 5)));
    }
    stop() {
        this.stopped = true; this.abort?.abort(); this.socket?.close();
        clearTimeout(this.timeout); clearTimeout(this.silence); clearTimeout(this.retryTimer); clearTimeout(this.hintTimer); clearTimeout(this.logoutProbe);
        clearInterval(this.ping); clearInterval(this.poll);
        document.removeEventListener("visibilitychange", this.resume);
        document.removeEventListener("submit", this.logout, true);
        this.channel?.close();
    }
}
