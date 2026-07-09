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
    },
    {
        constant: true,
        inputs: [
            { name: "owner", type: "address" },
            { name: "spender", type: "address" }
        ],
        name: "allowance",
        outputs: [{ name: "", type: "uint256" }],
        type: "function"
    }
];

const cafeFaucetAbi = [
    {
        constant: false,
        inputs: [],
        name: "claim",
        outputs: [],
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
    },
    {
        constant: false,
        inputs: [],
        name: "getReward",
        outputs: [],
        type: "function"
    },
    {
        constant: false,
        inputs: [],
        name: "claimReward",
        outputs: [],
        type: "function"
    }
];

let web3Instance = null;
let txNotifier = null;

async function notify(flow, step, status, txHash = null, message = null) {
    if (!txNotifier) {
        console.warn(`Tx status (no notifier): ${flow}/${step} → ${status}`, message ?? "");
        return;
    }

    try {
        await txNotifier.invokeMethodAsync("OnTxStatusChanged", flow, step, status, txHash, message);
    } catch (error) {
        console.warn("Could not deliver tx status to the app.", error);
    }
}

async function notifyCompleted(flow) {
    if (!txNotifier) {
        return;
    }

    try {
        await txNotifier.invokeMethodAsync("OnTxCompleted", flow);
    } catch (error) {
        console.warn("Could not deliver tx completion to the app.", error);
    }
}

function friendlyError(error, fallback) {
    if (error?.code === 4001 || error?.innerError?.code === 4001) {
        return "Transaction rejected in MetaMask.";
    }

    return error?.message ?? fallback;
}

function clearInput(inputId) {
    const input = document.getElementById(inputId);
    if (input) {
        input.value = "";
    }
}

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
    return providers.find(provider => provider?._metamask && isMetaMaskProvider(provider))
        ?? providers.find(isMetaMaskProvider)
        ?? null;
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

export async function connectWalletForStaking(config) {
    const web3 = getWeb3();
    const accounts = await web3.eth.requestAccounts();
    const account = accounts[0];
    if (!account) {
        throw new Error("No MetaMask account was selected.");
    }

    await ensureConfiguredNetwork(config);
    const chainId = Number(await web3.eth.net.getId());

    const payload = { walletAddress: account, chainId };

    if (!config.isWalletAuthenticated) {
        const challenge = await postJson("/api/wallet-auth/challenge", { address: account, walletName: "MetaMask" });
        const signature = await getMetaMaskProvider().request({
            method: "personal_sign",
            params: [challenge.message, account]
        });

        payload.signature = signature;
        payload.message = challenge.message;
        payload.nonce = challenge.nonce;
    }

    await sendJson("/staking/save-wallet-session", payload, true);
    return account;
}

function getCsrfToken() {
    const match = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/);
    return match ? decodeURIComponent(match[1]) : null;
}

async function sendJson(url, payload, includeCsrf = false) {
    const headers = { "Content-Type": "application/json" };
    if (includeCsrf) {
        const csrfToken = getCsrfToken();
        if (csrfToken) {
            headers["X-CSRF-TOKEN"] = csrfToken;
        }
    }

    const response = await fetch(url, {
        method: "POST",
        credentials: "same-origin",
        headers,
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Request failed.");
    }

    return response;
}

async function postJson(url, payload, includeCsrf = false) {
    const response = await sendJson(url, payload, includeCsrf);
    return response.json();
}

function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

async function postJsonWithConfirmationRetry(url, payload, flow, step = "record", maxAttempts = 10, delayMs = 3000) {
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        const response = await sendJson(url, payload, true);

        if (response.status === 202) {
            const pending = await response.json();
            await notify(
                flow,
                step,
                "pending",
                null,
                `Waiting for confirmations (${pending.confirmations}/${pending.requiredConfirmations})...`);
            await delay(delayMs);
            continue;
        }

        return response.json();
    }

    throw new Error("Timed out waiting for enough block confirmations. Check the transaction on the explorer and try again shortly.");
}

export function initCoffeePurchases(config, dotNetRef) {
    txNotifier = dotNetRef ?? txNotifier;

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
    bindFaucetActions(config, web3);

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

            let step = "payment";
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

                await notify("purchase", "payment", "pending", null, `Paying ${price} ${paymentTokenLabel} on ${networkName}.`);

                const paymentReceipt = await tokenContract.methods
                    .transfer(recipient, valueInWei)
                    .send({ from: activeAccount });

                await notify("purchase", "payment", "confirmed", paymentReceipt.transactionHash);

                step = "mint";
                await notify("purchase", "mint", "pending");
                const mintResult = await mintLoyaltyReward(
                    activeAccount,
                    reward,
                    price,
                    paymentReceipt.transactionHash,
                    allocationName
                );

                await notify("purchase", "mint", "confirmed", null, `Minted ${mintResult.mintedAmount ?? reward} COFFEE.`);
                await notifyCompleted("purchase");
            } catch (error) {
                await notify("purchase", step, "error", null, friendlyError(error, "Transaction failed."));
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });
}

function bindStakingActions(config, web3, paymentTokenAddress, paymentTokenLabel, networkName) {
    const stakingPoolAddress = config.stakingPoolContract;

    bindMaxButtons();

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

            let step = "approve";
            try {
                let amount;
                try {
                    amount = readTokenAmount("stake-amount", "Enter an amount to stake.");
                } catch (validationError) {
                    await notify("stake", "validate", "error", null, validationError.message);
                    return;
                }

                const activeAccount = await prepareWalletForTransaction(config, web3);
                const amountWei = web3.utils.toWei(amount, "ether");
                const tokenContract = new web3.eth.Contract(erc20ApproveAbi, paymentTokenAddress);
                const stakingContract = new web3.eth.Contract(stakingPoolAbi, stakingPoolAddress);

                const existingAllowance = await tokenContract.methods
                    .allowance(activeAccount, stakingPoolAddress)
                    .call();

                if (BigInt(existingAllowance) >= BigInt(amountWei)) {
                    await notify("stake", "approve", "confirmed", null, "Existing allowance already covers this amount.");
                } else {
                    await notify("stake", "approve", "pending", null, `Approve ${amount} ${paymentTokenLabel} in MetaMask.`);
                    const approvalReceipt = await tokenContract.methods
                        .approve(stakingPoolAddress, amountWei)
                        .send({ from: activeAccount });
                    await notify("stake", "approve", "confirmed", approvalReceipt.transactionHash);
                }

                step = "stake";
                await notify("stake", "stake", "pending", null, `Stake ${amount} ${paymentTokenLabel} in MetaMask.`);
                const receipt = await stakingContract.methods
                    .stake(amountWei)
                    .send({ from: activeAccount });
                await notify("stake", "stake", "confirmed", receipt.transactionHash);

                step = "record";
                await notify("stake", "record", "pending");
                await recordStakingTransaction("/staking/api/record-stake", activeAccount, amount, receipt.transactionHash, "stake");
                await notify("stake", "record", "confirmed", receipt.transactionHash);

                clearInput("stake-amount");
                await notifyCompleted("stake");
            } catch (error) {
                await notify("stake", step, "error", null, friendlyError(error, "Stake transaction failed."));
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

            let step = "unstake";
            try {
                let amount;
                try {
                    amount = readTokenAmount("unstake-amount", "Enter an amount to unstake.");

                    const input = document.getElementById("unstake-amount");
                    const stakedBalance = Number(input?.max || 0);
                    if (stakedBalance > 0 && Number(amount) > stakedBalance) {
                        throw new Error(`You can unstake up to ${stakedBalance} ${paymentTokenLabel}.`);
                    }
                } catch (validationError) {
                    await notify("unstake", "validate", "error", null, validationError.message);
                    return;
                }

                const activeAccount = await prepareWalletForTransaction(config, web3);
                const amountWei = web3.utils.toWei(amount, "ether");
                const stakingContract = new web3.eth.Contract(stakingPoolAbi, stakingPoolAddress);

                await notify("unstake", "unstake", "pending", null, `Unstake ${amount} ${paymentTokenLabel} in MetaMask.`);
                const receipt = await stakingContract.methods
                    .unstake(amountWei)
                    .send({ from: activeAccount });
                await notify("unstake", "unstake", "confirmed", receipt.transactionHash);

                step = "record";
                await notify("unstake", "record", "pending");
                await recordStakingTransaction("/staking/api/record-unstake", activeAccount, amount, receipt.transactionHash, "unstake");
                await notify("unstake", "record", "confirmed", receipt.transactionHash);

                clearInput("unstake-amount");
                await notifyCompleted("unstake");
            } catch (error) {
                await notify("unstake", step, "error", null, friendlyError(error, "Unstake transaction failed."));
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });

    document.querySelectorAll(".btn-claim-token").forEach((button) => {
        if (button.dataset.coffeeStakingBound === "true") {
            return;
        }

        button.dataset.coffeeStakingBound = "true";
        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            if (target.dataset.transactionPending === "true") {
                return;
            }

            const claimFunctionName = target.dataset.claimFunction;
            if (!claimFunctionName) {
                return;
            }

            target.dataset.transactionPending = "true";
            target.disabled = true;

            let step = "claim";
            try {
                const activeAccount = await prepareWalletForTransaction(config, web3);
                const stakingContract = new web3.eth.Contract(stakingPoolAbi, stakingPoolAddress);

                await notify("claim", "claim", "pending", null, "Claim COFFEE rewards in MetaMask.");
                const receipt = await stakingContract.methods[claimFunctionName]()
                    .send({ from: activeAccount });
                await notify("claim", "claim", "confirmed", receipt.transactionHash);

                step = "record";
                await notify("claim", "record", "pending");
                await recordStakingClaim(activeAccount, receipt.transactionHash);
                await notify("claim", "record", "confirmed", receipt.transactionHash);

                await notifyCompleted("claim");
            } catch (error) {
                await notify("claim", step, "error", null, friendlyError(error, "Claim transaction failed."));
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });
}

function bindFaucetActions(config, web3) {
    const cafeFaucetAddress = config.cafeFaucetContract;

    if (!cafeFaucetAddress || cafeFaucetAddress === "0x0000000000000000000000000000000000000000") {
        console.warn("CafeFaucetContract is not configured; the CAFE faucet is disabled.");
        return;
    }

    document.querySelectorAll(".btn-claim-cafe-faucet").forEach((button) => {
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

            let step = "claim";
            try {
                const activeAccount = await prepareWalletForTransaction(config, web3);
                const faucetContract = new web3.eth.Contract(cafeFaucetAbi, cafeFaucetAddress);

                await notify("cafe-faucet", "claim", "pending", null, "Claim test CAFE in MetaMask.");
                const receipt = await faucetContract.methods
                    .claim()
                    .send({ from: activeAccount });
                await notify("cafe-faucet", "claim", "confirmed", receipt.transactionHash);

                await notifyCompleted("cafe-faucet");
            } catch (error) {
                await notify("cafe-faucet", step, "error", null, friendlyError(error, "Claim transaction failed."));
            } finally {
                delete target.dataset.transactionPending;
                target.disabled = false;
            }
        });
    });
}

function bindMaxButtons() {
    document.querySelectorAll(".yield-max-btn").forEach((button) => {
        if (button.dataset.coffeeMaxBound === "true") {
            return;
        }

        button.dataset.coffeeMaxBound = "true";
        button.addEventListener("click", (event) => {
            const target = event.currentTarget;
            const input = target.dataset.target ? document.getElementById(target.dataset.target) : null;
            const max = target.dataset.max;

            if (input && max) {
                input.value = max;
                input.dispatchEvent(new Event("input", { bubbles: true }));
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
    try {
        return await postJsonWithConfirmationRetry("/Rewards/api/mint-loyalty", {
            walletAddress,
            amount,
            paymentAmount,
            paymentTransactionHash,
            allocationName
        }, "purchase", "mint");
    } catch (error) {
        throw new Error(error.message || "Payment succeeded, but loyalty minting failed.");
    }
}

async function recordStakingTransaction(endpoint, walletAddress, amount, transactionHash, flow) {
    try {
        return await postJsonWithConfirmationRetry(endpoint, { walletAddress, amount, transactionHash }, flow);
    } catch (error) {
        throw new Error(error.message || "Staking transaction succeeded, but server verification failed.");
    }
}

async function recordStakingClaim(walletAddress, transactionHash) {
    try {
        return await postJsonWithConfirmationRetry("/staking/api/record-claim", { walletAddress, transactionHash }, "claim");
    } catch (error) {
        throw new Error(error.message || "Claim transaction succeeded, but server verification failed.");
    }
}

function shortAddress(address) {
    return `${address.slice(0, 6)}...${address.slice(-4)}`;
}
