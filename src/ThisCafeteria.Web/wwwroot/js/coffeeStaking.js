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

const erc20ApproveAbi = [
    {
        constant: false,
        inputs: [
            { name: "spender", type: "address" },
            { name: "amount", type: "uint256" }
        ],
        name: "approve",
        outputs: [{ name: "", type: "bool" }],
        type: "function"
    }
];

const stakingPoolAbi = [
    {
        constant: false,
        inputs: [{ name: "amount", type: "uint256" }],
        name: "stake",
        outputs: [],
        type: "function"
    },
    {
        constant: false,
        inputs: [{ name: "amount", type: "uint256" }],
        name: "unstake",
        outputs: [],
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

    bindStakingActions(config, web3, paymentTokenAddress, paymentTokenLabel, networkName);

    document.querySelectorAll(".btn-buy-token").forEach((button) => {
        if (button.dataset.coffeeStakingBound === "true") {
            return;
        }

        button.dataset.coffeeStakingBound = "true";

        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            if (target.dataset.transactionPending === "true") {
                return;
            }

            const price = target.getAttribute("data-price");
            const reward = target.getAttribute("data-reward");
            const allocationName = target.getAttribute("data-allocation");

            if (!price || !reward) {
                return;
            }

            target.dataset.transactionPending = "true";
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
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });
}

function bindStakingActions(config, web3, paymentTokenAddress, paymentTokenLabel, networkName) {
    const stakingPoolAddress = config.stakingPoolContract;

    if (!stakingPoolAddress || stakingPoolAddress === "0x0000000000000000000000000000000000000000") {
        console.warn("StakingPoolContract is not configured; staking is disabled.");
        return;
    }

    document.querySelectorAll(".btn-stake-token").forEach((button) => {
        if (button.dataset.coffeeStakingBound === "true") {
            return;
        }

        button.dataset.coffeeStakingBound = "true";
        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            if (target.dataset.transactionPending === "true") {
                return;
            }

            target.dataset.transactionPending = "true";
            target.disabled = true;

            try {
                const amount = readTokenAmount("stake-amount", "Enter an amount to stake.");
                const activeAccount = await prepareWalletForTransaction(config, web3);
                const amountWei = web3.utils.toWei(amount, "ether");
                const tokenContract = new web3.eth.Contract(erc20ApproveAbi, paymentTokenAddress);
                const stakingContract = new web3.eth.Contract(stakingPoolAbi, stakingPoolAddress);

                window.alert(`Approve ${amount} ${paymentTokenLabel} for staking on ${networkName}. Confirm in MetaMask...`);
                await tokenContract.methods
                    .approve(stakingPoolAddress, amountWei)
                    .send({ from: activeAccount });

                window.alert(`Stake ${amount} ${paymentTokenLabel}. Confirm in MetaMask...`);
                const receipt = await stakingContract.methods
                    .stake(amountWei)
                    .send({ from: activeAccount });

                await recordStakingTransaction("/staking/api/record-stake", activeAccount, amount, receipt.transactionHash);
                window.alert(`Stake recorded. Transaction: ${shortAddress(receipt.transactionHash)}`);
                window.location.reload();
            } catch (error) {
                const message = error?.message ?? "Stake transaction failed.";
                window.alert(`Stake cancelled or failed: ${message}`);
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });

    document.querySelectorAll(".btn-unstake-token").forEach((button) => {
        if (button.dataset.coffeeStakingBound === "true") {
            return;
        }

        button.dataset.coffeeStakingBound = "true";
        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            if (target.dataset.transactionPending === "true") {
                return;
            }

            target.dataset.transactionPending = "true";
            target.disabled = true;

            try {
                const amount = readTokenAmount("unstake-amount", "Enter an amount to unstake.");
                const stakedBalance = Number(config.stakedTokenBalance ?? 0);
                if (Number(amount) > stakedBalance) {
                    throw new Error(`You can unstake up to ${stakedBalance} ${paymentTokenLabel}.`);
                }

                const activeAccount = await prepareWalletForTransaction(config, web3);
                const amountWei = web3.utils.toWei(amount, "ether");
                const stakingContract = new web3.eth.Contract(stakingPoolAbi, stakingPoolAddress);

                window.alert(`Unstake ${amount} ${paymentTokenLabel}. Confirm in MetaMask...`);
                const receipt = await stakingContract.methods
                    .unstake(amountWei)
                    .send({ from: activeAccount });

                await recordStakingTransaction("/staking/api/record-unstake", activeAccount, amount, receipt.transactionHash);
                window.alert(`Unstake recorded. Transaction: ${shortAddress(receipt.transactionHash)}`);
                window.location.reload();
            } catch (error) {
                const message = error?.message ?? "Unstake transaction failed.";
                window.alert(`Unstake cancelled or failed: ${message}`);
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });
}

async function prepareWalletForTransaction(config, web3) {
    await ensureConfiguredNetwork(config);
    const accounts = await web3.eth.requestAccounts();
    const activeAccount = accounts[0];

    if (!activeAccount) {
        throw new Error("No MetaMask account was selected.");
    }

    if (config.expectedWalletAddress &&
        activeAccount.toLowerCase() !== config.expectedWalletAddress.toLowerCase()) {
        throw new Error(`MetaMask is connected to ${shortAddress(activeAccount)}. Switch to ${shortAddress(config.expectedWalletAddress)} to use this staking session.`);
    }

    return activeAccount;
}

function readTokenAmount(inputId, missingMessage) {
    const input = document.getElementById(inputId);
    const amount = input?.value?.trim();

    if (!amount || Number(amount) <= 0) {
        throw new Error(missingMessage);
    }

    return amount;
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

async function recordStakingTransaction(endpoint, walletAddress, amount, transactionHash) {
    const response = await fetch(endpoint, {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            walletAddress,
            amount,
            transactionHash
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Staking transaction succeeded, but server verification failed.");
    }

    return response.json();
}

function shortAddress(address) {
    return `${address.slice(0, 6)}...${address.slice(-4)}`;
}
