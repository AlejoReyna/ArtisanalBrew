const apiBaseUrl = window.thisCafeteriaApiBaseUrl ?? "";

export async function publishWalletStatus(walletAddress, status, eventType = null, payload = null) {
    const response = await fetch(`${apiBaseUrl}/api/wallet-status`, {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            walletAddress,
            status,
            eventType,
            payload
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Wallet status could not be published.");
    }

    return response.json();
}

export async function getLatestWalletStatus(walletAddress) {
    const response = await fetch(`${apiBaseUrl}/api/wallet-status/${encodeURIComponent(walletAddress)}`, {
        credentials: "same-origin"
    });

    if (response.status === 404) {
        return null;
    }

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Wallet status could not be loaded.");
    }

    return response.json();
}
