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

const bnbTestnetRpcUrl = "https://rpc.ankr.com/bsc_testnet_chapel/56e119a6270f4441ea452c1756c15ec402eb41bcb0965b5cb4b0fec0a6b4cb51";

let web3Instance = null;

function getWeb3() {
    if (!window.ethereum) {
        throw new Error("MetaMask is not installed.");
    }

    if (!web3Instance) {
        web3Instance = new Web3(window.ethereum);
    }

    return web3Instance;
}

export async function connectWalletForStaking() {
    const web3 = getWeb3();
    const accounts = await web3.eth.requestAccounts();
    const account = accounts[0];

    const response = await fetch("/staking/save-wallet-session", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ walletAddress: account })
    });

    if (!response.ok) {
        throw new Error("Could not save the wallet session.");
    }

    return account;
}

export function initCoffeePurchases(config) {
    if (!window.ethereum) {
        return;
    }

    const web3 = getWeb3();
    const recipient = config.marketplaceWallet;
    const ankrBnbAddress = config.ankrBnbContract;
    const chainIdHex = config.chainIdHex;

    if (!recipient || recipient === "0x0000000000000000000000000000000000000000") {
        console.warn("MarketplaceWallet is not configured; coffee purchases are disabled.");
        return;
    }

    document.querySelectorAll(".btn-buy-ankr").forEach((button) => {
        button.addEventListener("click", async (event) => {
            const target = event.currentTarget;
            const price = target.getAttribute("data-price");
            const reward = target.getAttribute("data-reward");

            if (!price || !reward) {
                return;
            }

            target.disabled = true;

            try {
                const accounts = await web3.eth.getAccounts();
                if (!accounts.length) {
                    await web3.eth.requestAccounts();
                }

                const activeAccount = (await web3.eth.getAccounts())[0];
                const networkId = Number(await web3.eth.net.getId());

                if (networkId !== config.chainId) {
                    await ensureBnbTestnet(chainIdHex);
                }

                const tokenContract = new web3.eth.Contract(erc20TransferAbi, ankrBnbAddress);
                const valueInWei = web3.utils.toWei(price, "ether");

                window.alert(`Processing payment of ${price} ankrBNB. Confirm in MetaMask...`);

                const paymentReceipt = await tokenContract.methods
                    .transfer(recipient, valueInWei)
                    .send({ from: activeAccount });

                const mintResult = await mintLoyaltyReward(
                    activeAccount,
                    reward,
                    price,
                    paymentReceipt.transactionHash
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

async function ensureBnbTestnet(chainIdHex) {
    try {
        await window.ethereum.request({
            method: "wallet_switchEthereumChain",
            params: [{ chainId: chainIdHex }]
        });
    } catch (error) {
        if (error?.code !== 4902) {
            throw error;
        }

        await window.ethereum.request({
            method: "wallet_addEthereumChain",
            params: [{
                chainId: chainIdHex,
                chainName: "BSC Testnet",
                nativeCurrency: {
                    name: "Test BNB",
                    symbol: "tBNB",
                    decimals: 18
                },
                rpcUrls: [bnbTestnetRpcUrl],
                blockExplorerUrls: ["https://testnet.bscscan.com/"]
            }]
        });
    }
}

async function mintLoyaltyReward(walletAddress, amount, paymentAmount, paymentTransactionHash) {
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
            paymentTransactionHash
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || "Payment succeeded, but loyalty minting failed.");
    }

    return response.json();
}
