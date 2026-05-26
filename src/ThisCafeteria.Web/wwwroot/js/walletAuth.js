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

        const challenge = await postJson("/api/wallet-auth/challenge", { address, walletName });
        await ensureConfiguredNetwork(provider, challenge);

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
        return providers.find(provider => provider.isMetaMask && !provider.isPhantom) ?? null;
    }

    if (normalizedName.includes("coinbase")) {
        return providers.find(provider => provider.isCoinbaseWallet) ?? window.ethereum;
    }

    return window.ethereum;
}

async function ensureConfiguredNetwork(provider, config) {
    const network = buildWalletNetwork(config);
    const currentChainId = await provider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === network.chainId) {
        return;
    }

    try {
        await provider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: network.chainId }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await provider.request({
            method: "wallet_addEthereumChain",
            params: [network]
        });
    }
}

function buildWalletNetwork(config) {
    const chainId = config.chainIdHex ?? `0x${Number(config.chainId).toString(16)}`;

    return {
        chainId: chainId.toLowerCase(),
        chainName: config.networkName ?? "Ethereum Sepolia",
        nativeCurrency: {
            name: config.currencyName ?? "Sepolia ETH",
            symbol: config.currencySymbol ?? "ETH",
            decimals: config.currencyDecimals ?? 18
        },
        rpcUrls: [config.rpcUrl],
        blockExplorerUrls: [config.explorerUrl]
    };
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
