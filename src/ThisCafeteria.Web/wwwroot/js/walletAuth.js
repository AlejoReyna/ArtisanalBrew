export async function loginWithWallet(walletName = "wallet") {
    const provider = await getWalletProvider(walletName);
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

async function getWalletProvider(walletName) {
    const providers = await getAvailableProviders();
    const normalizedName = walletName.toLowerCase();

    if (normalizedName.includes("metamask")) {
        return findMetaMaskProvider(providers);
    }

    if (normalizedName.includes("coinbase")) {
        return providers.find(provider => provider.isCoinbaseWallet) ?? window.ethereum;
    }

    if (normalizedName.includes("phantom")) {
        return providers.find(provider => provider.isPhantom)
            ?? window.phantom?.ethereum
            ?? null;
    }

    if (normalizedName.includes("brave")) {
        return providers.find(provider => provider.isBraveWallet)
            ?? (window.ethereum?.isBraveWallet ? window.ethereum : null);
    }

    if (normalizedName.includes("trust")) {
        return providers.find(provider => provider.isTrust || provider.isTrustWallet)
            ?? window.trustwallet
            ?? null;
    }

    if (normalizedName.includes("other")) {
        return providers.find(provider => !isDisplayedWalletProvider(provider)) ?? window.ethereum ?? null;
    }

    return findMetaMaskProvider(providers) ?? window.ethereum ?? providers[0] ?? null;
}

async function getAvailableProviders() {
    const injectedProviders = window.ethereum?.providers ?? (window.ethereum ? [window.ethereum] : []);
    const announcedProviders = await getAnnouncedProviders();
    const announcedMetaMaskProviders = announcedProviders
        .filter(isMetaMaskAnnouncement)
        .map(announcement => announcement.provider);
    const otherAnnouncedProviders = announcedProviders
        .filter(announcement => !isMetaMaskAnnouncement(announcement))
        .map(announcement => announcement.provider);
    const providers = [
        ...announcedMetaMaskProviders,
        ...injectedProviders.filter(provider => provider?._metamask),
        ...injectedProviders.filter(isMetaMaskProvider),
        ...otherAnnouncedProviders,
        ...injectedProviders
    ];

    return providers.filter((provider, index) => provider && providers.indexOf(provider) === index);
}

function getAnnouncedProviders() {
    if (typeof window === "undefined") {
        return Promise.resolve([]);
    }

    return new Promise(resolve => {
        const providers = [];
        const onAnnouncement = event => {
            if (event.detail?.provider) {
                providers.push(event.detail);
            }
        };

        window.addEventListener("eip6963:announceProvider", onAnnouncement);
        window.dispatchEvent(new Event("eip6963:requestProvider"));

        window.setTimeout(() => {
            window.removeEventListener("eip6963:announceProvider", onAnnouncement);
            resolve(providers);
        }, 80);
    });
}

function findMetaMaskProvider(providers) {
    return providers.find(provider => provider?._metamask && isMetaMaskProvider(provider))
        ?? providers.find(isMetaMaskProvider)
        ?? null;
}

function isMetaMaskAnnouncement(announcement) {
    const rdns = announcement?.info?.rdns?.toLowerCase();
    const name = announcement?.info?.name?.toLowerCase();

    return Boolean(
        rdns === "io.metamask" ||
        rdns?.startsWith("io.metamask.") ||
        name === "metamask"
    );
}

function isMetaMaskProvider(provider) {
    return Boolean(
        provider?.isMetaMask &&
        !isKnownNonMetaMaskProvider(provider)
    );
}

function isKnownNonMetaMaskProvider(provider) {
    return Boolean(
        provider?.isPhantom ||
        provider?.isCoinbaseWallet ||
        provider?.isBraveWallet ||
        provider?.isTrust ||
        provider?.isTrustWallet ||
        provider?.isRabby ||
        provider?.isRabbyWallet ||
        provider?.isOkxWallet ||
        provider?.isOKExWallet ||
        provider?.isBitKeep ||
        provider?.isBitgetWallet
    );
}

function isDisplayedWalletProvider(provider) {
    return Boolean(
        isMetaMaskProvider(provider) ||
        provider?.isPhantom ||
        provider?.isCoinbaseWallet ||
        provider?.isBraveWallet
    );
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
