const bnbTestnet = {
    chainId: "0x61",
    chainName: "BSC Testnet",
    nativeCurrency: {
        name: "Test BNB",
        symbol: "tBNB",
        decimals: 18
    },
    rpcUrls: ["https://rpc.ankr.com/bsc_testnet_chapel"],
    blockExplorerUrls: ["https://testnet.bscscan.com/"]
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

        await ensureBnbTestnet(provider);

        const challenge = await postJson("/api/wallet-auth/challenge", { address });
        const signature = await provider.request({
            method: "personal_sign",
            params: [challenge.message, address]
        });

        const verification = await postJson("/api/wallet-auth/verify", {
            address,
            signature,
            message: challenge.message,
            nonce: challenge.nonce,
            chainId: challenge.chainId
        });

        return {
            success: verification.success,
            address: verification.address,
            redirectUrl: verification.redirectUrl
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

async function ensureBnbTestnet(provider) {
    const currentChainId = await provider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === bnbTestnet.chainId) {
        return;
    }

    try {
        await provider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: bnbTestnet.chainId }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await provider.request({
            method: "wallet_addEthereumChain",
            params: [bnbTestnet]
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
