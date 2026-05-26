export async function payWithNativeEth(config) {
    const provider = getMetaMaskProvider();
    if (!provider) {
        throw new Error("MetaMask is not installed.");
    }

    const accounts = await provider.request({ method: "eth_requestAccounts" });
    const fromAddress = accounts?.[0];
    if (!fromAddress) {
        throw new Error("No wallet account was selected.");
    }

    if (config.expectedWalletAddress &&
        fromAddress.toLowerCase() !== config.expectedWalletAddress.toLowerCase()) {
        throw new Error(`MetaMask is connected to ${shortAddress(fromAddress)}. Switch to ${shortAddress(config.expectedWalletAddress)} to pay for this account.`);
    }

    await ensureConfiguredNetwork(config);

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

function getMetaMaskProvider() {
    const providers = window.ethereum?.providers ?? (window.ethereum ? [window.ethereum] : []);
    return providers.find(provider => provider.isMetaMask && !provider.isPhantom) ?? null;
}

async function ensureConfiguredNetwork(config) {
    const provider = getMetaMaskProvider();
    const expectedChainId = config.chainIdHex.toLowerCase();
    const currentChainId = await provider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === expectedChainId) {
        return;
    }

    try {
        await provider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: expectedChainId }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await provider.request({
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
