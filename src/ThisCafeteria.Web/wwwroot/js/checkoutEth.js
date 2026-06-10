export async function connectWalletForCheckout(config) {
    const provider = await resolveMetaMaskProvider();
    if (!provider) {
        throw new Error("MetaMask is not installed.");
    }

    const accounts = await provider.request({ method: "eth_requestAccounts" });
    const fromAddress = accounts?.[0];
    if (!fromAddress) {
        throw new Error("No wallet account was selected.");
    }

    validateExpectedWallet(fromAddress, config.expectedWalletAddress);
    await ensureConfiguredNetwork(config, provider);

    return fromAddress;
}

export async function payWithNativeEth(config) {
    const provider = await resolveMetaMaskProvider();
    if (!provider) {
        throw new Error("MetaMask is not installed.");
    }

    const accounts = await provider.request({ method: "eth_requestAccounts" });
    const fromAddress = accounts?.[0];
    if (!fromAddress) {
        throw new Error("No wallet account was selected.");
    }

    validateExpectedWallet(fromAddress, config.expectedWalletAddress);
    await ensureConfiguredNetwork(config, provider);

    const amountWei = parseEtherToWei(config.amountEth);
    const balanceWei = BigInt(await provider.request({
        method: "eth_getBalance",
        params: [fromAddress, "latest"]
    }));
    const gasPriceWei = BigInt(await provider.request({ method: "eth_gasPrice" }));
    const estimatedGasWei = 21000n * gasPriceWei;
    const requiredWei = amountWei + estimatedGasWei;

    if (balanceWei < requiredWei) {
        throw new Error(
            `Insufficient Sepolia ETH. Required ${formatWei(requiredWei)} ETH including estimated gas; wallet has ${formatWei(balanceWei)} ETH.`
        );
    }

    const transactionHash = await provider.request({
        method: "eth_sendTransaction",
        params: [{
            from: fromAddress,
            to: config.marketplaceWallet,
            value: toHex(amountWei)
        }]
    });

    return {
        transactionHash,
        fromAddress
    };
}

async function resolveMetaMaskProvider() {
    const providers = await getAvailableProviders();
    return findMetaMaskProvider(providers);
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

function validateExpectedWallet(fromAddress, expectedWalletAddress) {
    if (expectedWalletAddress &&
        fromAddress.toLowerCase() !== expectedWalletAddress.toLowerCase()) {
        throw new Error(`MetaMask is connected to ${shortAddress(fromAddress)}. Switch to ${shortAddress(expectedWalletAddress)} to pay for this account.`);
    }
}

async function ensureConfiguredNetwork(config, provider) {
    const activeProvider = provider ?? await resolveMetaMaskProvider();
    const expectedChainId = config.chainIdHex.toLowerCase();
    const currentChainId = await activeProvider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === expectedChainId) {
        return;
    }

    try {
        await activeProvider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: expectedChainId }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await activeProvider.request({
            method: "wallet_addEthereumChain",
            params: [{
                chainId: expectedChainId,
                chainName: config.networkName,
                nativeCurrency: {
                    name: config.currencyName,
                    symbol: config.currencySymbol,
                    decimals: config.currencyDecimals
                },
                rpcUrls: [config.rpcUrl],
                blockExplorerUrls: [config.explorerUrl]
            }]
        });
    }
}

function parseEtherToWei(value) {
    const normalized = String(value).trim();
    if (!/^\d+(\.\d+)?$/.test(normalized)) {
        throw new Error("Invalid ETH amount.");
    }

    const [whole, fraction = ""] = normalized.split(".");
    const paddedFraction = `${fraction}000000000000000000`.slice(0, 18);
    return BigInt(whole) * 1000000000000000000n + BigInt(paddedFraction);
}

function toHex(value) {
    return `0x${value.toString(16)}`;
}

function formatWei(value) {
    const whole = value / 1000000000000000000n;
    const fraction = (value % 1000000000000000000n).toString().padStart(18, "0").slice(0, 6);
    return `${whole}.${fraction}`;
}

function shortAddress(address) {
    return `${address.slice(0, 6)}...${address.slice(-4)}`;
}
