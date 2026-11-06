window.velotimeLanguage = {
    async state() {
        const response = await fetch("/language/state", { credentials: "same-origin", cache: "no-store" });
        if (!response.ok) throw new Error("language.state.unavailable");
        return await response.json();
    },
    async select(language, returnUrl) {
        const tokenResponse = await fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store" });
        if (!tokenResponse.ok) throw new Error("language.antiforgery.unavailable");
        const token = await tokenResponse.json();
        const response = await fetch("/language/select", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
            body: JSON.stringify({ language, returnUrl })
        });
        if (!response.ok) throw new Error("language.preference.save_failed");
        const result = await response.json();
        window.location.assign(result.redirectUrl);
    }
};
