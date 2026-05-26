const erc20TransferAbi = [
    {
        constant: false,
        inputs: [
            { name: "recipient", type: "address" },
            { name: "amount", type: "uint256" }
        ],
        name: "transfer",
        outputs: [{ name: "", type: "bool" }],
        type: "function"
    }
];

let web3Instance = null;

function getWeb3() {
    const provider = getMetaMaskProvider();
    if (!provider) {
        throw new Error("MetaMask is not installed.");
    }

    if (!web3Instance) {
        web3Instance = new Web3(provider);
    }

    return web3Instance;
}

function getMetaMaskProvider() {
    const providers = window.ethereum?.providers ?? (window.ethereum ? [window.ethereum] : []);
    return providers.find(provider => provider.isMetaMask && !provider.isPhantom) ?? null;
}

export async function connectWalletForStaking(config) {
    const web3 = getWeb3();
    const accounts = await web3.eth.requestAccounts();
    const account = accounts[0];
    if (!account) {
        throw new Error("No MetaMask account was selected.");
    }

    await ensureConfiguredNetwork(config);
    const chainId = Number(await web3.eth.net.getId());

    const response = await fetch("/staking/save-wallet-session", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ walletAddress: account, chainId })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Could not save the wallet session.");
    }

    return account;
}

export function initCoffeePurchases(config) {
    if (!getMetaMaskProvider()) {
        return;
    }

    const web3 = getWeb3();
    const recipient = config.marketplaceWallet;
    const paymentTokenAddress = config.paymentTokenContract;
    const networkName = config.networkName ?? "Ethereum Sepolia";
    const paymentTokenLabel = config.paymentTokenSymbol ?? "payment token";

    if (!recipient || recipient === "0x0000000000000000000000000000000000000000") {
        console.warn("MarketplaceWallet is not configured; coffee purchases are disabled.");
        return;
    }

    if (!paymentTokenAddress || paymentTokenAddress === "0x0000000000000000000000000000000000000000") {
        console.warn("PaymentTokenContract is not configured; coffee purchases are disabled.");
        return;
    }

    document.querySelectorAll(".btn-buy-token").forEach((button) => {
        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            const price = target.getAttribute("data-price");
            const reward = target.getAttribute("data-reward");
            const allocationName = target.getAttribute("data-allocation");

            if (!price || !reward) {
                return;
            }

            target.disabled = true;

            try {
                await ensureConfiguredNetwork(config);
                const accounts = await web3.eth.requestAccounts();
                const activeAccount = accounts[0];

                if (config.expectedWalletAddress &&
                    activeAccount.toLowerCase() !== config.expectedWalletAddress.toLowerCase()) {
                    throw new Error(`MetaMask is connected to ${shortAddress(activeAccount)}. Switch to ${shortAddress(config.expectedWalletAddress)} to allocate from this session.`);
                }

                const tokenContract = new web3.eth.Contract(erc20TransferAbi, paymentTokenAddress);
                const valueInWei = web3.utils.toWei(price, "ether");

                window.alert(`Processing payment of ${price} ${paymentTokenLabel} on ${networkName}. Confirm in MetaMask...`);

                const paymentReceipt = await tokenContract.methods
                    .transfer(recipient, valueInWei)
                    .send({ from: activeAccount });

                const mintResult = await mintLoyaltyReward(
                    activeAccount,
                    reward,
                    price,
                    paymentReceipt.transactionHash,
                    allocationName
                );

                window.alert(
                    `Payment successful. Your coffee is on the way. Minted ${mintResult.mintedAmount ?? reward} COFFEE.`
                );

                window.location.reload();
            } catch (error) {
                const message = error?.message ?? "Transaction failed.";
                window.alert(`Transaction cancelled or failed: ${message}`);
            } finally {
                target.disabled = false;
            }
        });
    });
}

async function ensureConfiguredNetwork(config) {
    if (!config) {
        return;
    }

    const provider = getMetaMaskProvider();
    const chainIdHex = config.chainIdHex;
    const currentChainId = await provider.request({ method: "eth_chainId" });
    if (currentChainId?.toLowerCase() === chainIdHex?.toLowerCase()) {
        return;
    }

    try {
        await provider.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: chainIdHex }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await provider.request({
            method: "wallet_addEthereumChain",
            params: [{
                chainId: chainIdHex,
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

async function mintLoyaltyReward(walletAddress, amount, paymentAmount, paymentTransactionHash, allocationName) {
    const response = await fetch("/Rewards/api/mint-loyalty", {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            walletAddress,
            amount,
            paymentAmount,
            paymentTransactionHash,
            allocationName
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Payment succeeded, but loyalty minting failed.");
    }

    return response.json();
}

function shortAddress(address) {
    return `${address.slice(0, 6)}...${address.slice(-4)}`;
}
