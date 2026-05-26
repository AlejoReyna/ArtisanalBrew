const baseSepolia = {
    chainId: "0x14a34",
    chainName: "Base Sepolia",
    nativeCurrency: {
        name: "Sepolia ETH",
        symbol: "ETH",
        decimals: 18
    },
    rpcUrls: ["https://sepolia.base.org"],
    blockExplorerUrls: ["https://sepolia-explorer.base.org/"]
};

export async function loginWithWallet(walletName = "wallet") {
    const provider = getWalletProvider(walletName);
    if (!provider) {
        return {
            success: false,
            error: `${walletName} was not found. Install or enable it to continue.`
        };
    }

    try {
        const accounts = await provider.request({ method: "eth_requestAccounts" });
        const address = accounts?.[0];
        if (!address) {
            return { success: false, error: "No wallet account was selected." };
        }

        await ensureBaseSepolia(provider);

        const challenge = await postJson("/api/wallet-auth/challenge", { address, walletName });
        const signature = await provider.request({
            method: "personal_sign",
            params: [challenge.message, address]
        });

        const verification = await postJson("/api/wallet-auth/verify", {
            address,
            signature,
            message: challenge.message,
            nonce: challenge.nonce,
            chainId: challenge.chainId,
            walletName
        });

        return {
            success: verification.success,
            address: verification.address,
            redirectUrl: verification.redirectUrl,
            statusStored: verification.statusStored,
            statusPublished: verification.statusPublished,
            awsMessageId: verification.awsMessageId
        };
    } catch (error) {
        return {
            success: false,
            error: friendlyWalletError(error)
        };
    }
}

function getWalletProvider(walletName) {
    const providers = window.ethereum?.providers ?? (window.ethereum ? [window.ethereum] : []);
    const normalizedName = walletName.toLowerCase();

    if (normalizedName.includes("metamask")) {
        return providers.find(provider => provider.isMetaMask) ?? window.ethereum;
    }

    if (normalizedName.includes("coinbase")) {
        return providers.find(provider => provider.isCoinbaseWallet) ?? window.ethereum;
    }

    return window.ethereum;
}

async function ensureBaseSepolia(provider) {
    const currentChainId = await provider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === baseSepolia.chainId) {
        return;
    }

    try {
        await provider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: baseSepolia.chainId }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await provider.request({
            method: "wallet_addEthereumChain",
            params: [baseSepolia]
        });
    }
}

async function postJson(url, payload) {
    const response = await fetch(url, {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Wallet login failed.");
    }

    return response.json();
}

function friendlyWalletError(error) {
    if (error?.code === 4001) {
        return "The wallet request was rejected.";
    }

    if (error instanceof Error && error.message) {
        return error.message;
    }

    return "Wallet login could not be completed.";
}
