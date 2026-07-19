window.artisanalChainSelector = {
    async persist(chainKey) {
        const token = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/)?.[1];
        const response = await fetch("/api/chains/select", {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                ...(token ? { "X-CSRF-TOKEN": decodeURIComponent(token) } : {})
            },
            body: JSON.stringify({ chainKey })
        });
        if (!response.ok) throw new Error(await response.text() || "Network selection could not be saved.");
    }
};
