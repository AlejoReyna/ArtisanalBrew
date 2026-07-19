const erc20Abi = [
    { constant: true, inputs: [{ name: "owner", type: "address" }, { name: "spender", type: "address" }], name: "allowance", outputs: [{ name: "", type: "uint256" }], type: "function" },
    { constant: false, inputs: [{ name: "spender", type: "address" }, { name: "amount", type: "uint256" }], name: "approve", outputs: [{ name: "", type: "bool" }], type: "function" }
];
const vaultAbi = [
    { inputs: [{ name: "assets", type: "uint256" }, { name: "receiver", type: "address" }], name: "deposit", outputs: [{ name: "shares", type: "uint256" }], type: "function" },
    { inputs: [{ name: "shares", type: "uint256" }, { name: "receiver", type: "address" }, { name: "owner", type: "address" }], name: "redeem", outputs: [{ name: "assets", type: "uint256" }], type: "function" },
    { inputs: [], name: "claimRewards", outputs: [{ name: "reward", type: "uint256" }], type: "function" }
];

let notifier;
let activeConfig;

export function initLiquidStaking(config, dotNetRef) {
    activeConfig = config;
    notifier = dotNetRef;
    bind(".btn-liquid-deposit", "deposit", "liquid-deposit-amount");
    bind(".btn-liquid-redeem", "redeem", "liquid-redeem-amount");
    bind(".btn-liquid-claim", "claim", null);
}

function bind(selector, operation, inputId) {
    document.querySelectorAll(selector).forEach(button => {
        if (button.dataset.liquidBound === "true") return;
        button.dataset.liquidBound = "true";
        button.addEventListener("click", async () => {
            if (button.disabled || button.dataset.pending === "true") return;
            button.dataset.pending = "true";
            button.disabled = true;
            try {
                const amount = inputId ? document.getElementById(inputId)?.value?.trim() : null;
                if (inputId && (!amount || !/^\d+(\.\d+)?$/.test(amount) || /^0+(\.0+)?$/.test(amount))) throw new Error("Enter a positive token amount.");
                const provider = window.ethereum;
                if (!provider) throw new Error("An EVM wallet was not found.");
                const web3 = new Web3(provider);
                await switchChain(provider, activeConfig);
                const accounts = await web3.eth.requestAccounts();
                const wallet = accounts?.[0];
                if (!wallet || wallet.toLowerCase() !== activeConfig.expectedWalletAddress.toLowerCase()) throw new Error("The connected wallet does not match this session.");
                const vault = new web3.eth.Contract(vaultAbi, activeConfig.vaultContract);
                let baseAmount = amount ? web3.utils.toWei(amount, "ether") : null;
                if (operation === "deposit") {
                    const cafe = new web3.eth.Contract(erc20Abi, activeConfig.cafeContract);
                    const allowance = await cafe.methods.allowance(wallet, activeConfig.vaultContract).call();
                    if (web3.utils.toBN(allowance).lt(web3.utils.toBN(baseAmount))) {
                        await send("deposit", "approve", "pending", null, "Approve CAFE in your wallet.");
                        const approval = await cafe.methods.approve(activeConfig.vaultContract, baseAmount).send({ from: wallet });
                        await send("deposit", "approve", "confirmed", approval.transactionHash);
                    }
                    await send("deposit", "deposit", "pending", null, "Deposit CAFE and mint stCAFE.");
                    const receipt = await vault.methods.deposit(baseAmount, wallet).send({ from: wallet });
                    await send("deposit", "deposit", "confirmed", receipt.transactionHash);
                    await record("record-deposit", wallet, receipt.transactionHash, amount);
                } else if (operation === "redeem") {
                    await send("redeem", "redeem", "pending", null, "Redeem stCAFE for CAFE.");
                    const receipt = await vault.methods.redeem(baseAmount, wallet, wallet).send({ from: wallet });
                    await send("redeem", "redeem", "confirmed", receipt.transactionHash);
                    await record("record-redeem", wallet, receipt.transactionHash, amount);
                } else {
                    await send("claim", "claim", "pending", null, "Claim COFFEE rewards.");
                    const receipt = await vault.methods.claimRewards().send({ from: wallet });
                    await send("claim", "claim", "confirmed", receipt.transactionHash);
                    await record("record-claim", wallet, receipt.transactionHash, null);
                }
                await complete(operation);
            } catch (error) {
                await send(operation, operation, "error", null, error?.message || "Liquid-staking transaction failed.");
            } finally {
                delete button.dataset.pending;
                button.disabled = false;
            }
        });
    });
}

async function record(endpoint, walletIdentifier, transactionId, expectedAmount) {
    for (let attempt = 0; attempt < 10; attempt++) {
        const response = await fetch(`/staking/api/liquid/${endpoint}`, {
            method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf() },
            body: JSON.stringify({ chainKey: activeConfig.chainKey, walletIdentifier, transactionId, expectedAmount })
        });
        if (response.status === 202) { await new Promise(resolve => setTimeout(resolve, 3000)); continue; }
        if (!response.ok) throw new Error(await response.text() || "Server verification failed.");
        return response.json();
    }
    throw new Error("Timed out waiting for server transaction verification.");
}

async function switchChain(provider, config) {
    if ((await provider.request({ method: "eth_chainId" })).toLowerCase() === config.chainIdHex.toLowerCase()) return;
    try { await provider.request({ method: "wallet_switchEthereumChain", params: [{ chainId: config.chainIdHex }] }); }
    catch (error) {
        if (error?.code !== 4902) throw error;
        await provider.request({ method: "wallet_addEthereumChain", params: [{ chainId: config.chainIdHex, chainName: config.networkName, nativeCurrency: { name: config.currencyName, symbol: config.currencySymbol, decimals: config.currencyDecimals }, rpcUrls: [config.rpcUrl], blockExplorerUrls: [config.explorerUrl] }] });
    }
}

function csrf() { return decodeURIComponent(document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/)?.[1] || ""); }
async function send(flow, step, status, txHash, message) { if (notifier) await notifier.invokeMethodAsync("OnTxStatusChanged", flow, step, status, txHash, message); }
async function complete(flow) { if (notifier) await notifier.invokeMethodAsync("OnTxCompleted", flow); }
