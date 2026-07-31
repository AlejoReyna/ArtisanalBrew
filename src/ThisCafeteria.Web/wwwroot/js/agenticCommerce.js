
export const ESCROW_ABI = [{"inputs": [{"internalType": "contract IERC20", "name": "paymentToken_", "type": "address"}, {"internalType": "address", "name": "treasury_", "type": "address"}, {"internalType": "uint256", "name": "platformFeeBps_", "type": "uint256"}, {"internalType": "address", "name": "trustedForwarder_", "type": "address"}], "stateMutability": "nonpayable", "type": "constructor"}, {"inputs": [], "name": "BudgetMismatch", "type": "error"}, {"inputs": [], "name": "ExpiryTooShort", "type": "error"}, {"inputs": [], "name": "FeeTooHigh", "type": "error"}, {"inputs": [], "name": "InvalidJob", "type": "error"}, {"inputs": [], "name": "ProviderNotSet", "type": "error"}, {"inputs": [], "name": "ReentrancyGuardReentrantCall", "type": "error"}, {"inputs": [{"internalType": "address", "name": "token", "type": "address"}], "name": "SafeERC20FailedOperation", "type": "error"}, {"inputs": [], "name": "Unauthorized", "type": "error"}, {"inputs": [{"internalType": "uint256", "name": "expected", "type": "uint256"}, {"internalType": "uint256", "name": "received", "type": "uint256"}], "name": "Underfunded", "type": "error"}, {"inputs": [], "name": "WrongStatus", "type": "error"}, {"inputs": [], "name": "ZeroAddress", "type": "error"}, {"inputs": [], "name": "ZeroBudget", "type": "error"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": false, "internalType": "uint256", "name": "amount", "type": "uint256"}], "name": "BudgetSet", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "evaluator", "type": "address"}, {"indexed": false, "internalType": "bytes32", "name": "reason", "type": "bytes32"}], "name": "JobCompleted", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "client", "type": "address"}, {"indexed": true, "internalType": "address", "name": "provider", "type": "address"}, {"indexed": false, "internalType": "address", "name": "evaluator", "type": "address"}, {"indexed": false, "internalType": "uint256", "name": "expiredAt", "type": "uint256"}], "name": "JobCreated", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}], "name": "JobExpired", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "client", "type": "address"}, {"indexed": false, "internalType": "uint256", "name": "amount", "type": "uint256"}], "name": "JobFunded", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "rejector", "type": "address"}, {"indexed": false, "internalType": "bytes32", "name": "reason", "type": "bytes32"}], "name": "JobRejected", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "provider", "type": "address"}, {"indexed": false, "internalType": "bytes32", "name": "deliverable", "type": "bytes32"}], "name": "JobSubmitted", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "provider", "type": "address"}, {"indexed": false, "internalType": "uint256", "name": "amount", "type": "uint256"}], "name": "PaymentReleased", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "provider", "type": "address"}], "name": "ProviderSet", "type": "event"}, {"anonymous": false, "inputs": [{"indexed": true, "internalType": "uint256", "name": "jobId", "type": "uint256"}, {"indexed": true, "internalType": "address", "name": "client", "type": "address"}, {"indexed": false, "internalType": "uint256", "name": "amount", "type": "uint256"}], "name": "Refunded", "type": "event"}, {"inputs": [], "name": "MAX_FEE_BPS", "outputs": [{"internalType": "uint256", "name": "", "type": "uint256"}], "stateMutability": "view", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}], "name": "claimRefund", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "bytes32", "name": "reason", "type": "bytes32"}, {"internalType": "bytes", "name": "", "type": "bytes"}], "name": "complete", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "address", "name": "provider", "type": "address"}, {"internalType": "address", "name": "evaluator", "type": "address"}, {"internalType": "uint256", "name": "expiredAt", "type": "uint256"}, {"internalType": "string", "name": "description", "type": "string"}], "name": "createJob", "outputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "uint256", "name": "expectedBudget", "type": "uint256"}, {"internalType": "bytes", "name": "", "type": "bytes"}], "name": "fund", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "address", "name": "forwarder", "type": "address"}], "name": "isTrustedForwarder", "outputs": [{"internalType": "bool", "name": "", "type": "bool"}], "stateMutability": "view", "type": "function"}, {"inputs": [], "name": "jobCounter", "outputs": [{"internalType": "uint256", "name": "", "type": "uint256"}], "stateMutability": "view", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "", "type": "uint256"}], "name": "jobs", "outputs": [{"internalType": "uint256", "name": "id", "type": "uint256"}, {"internalType": "address", "name": "client", "type": "address"}, {"internalType": "address", "name": "provider", "type": "address"}, {"internalType": "address", "name": "evaluator", "type": "address"}, {"internalType": "string", "name": "description", "type": "string"}, {"internalType": "uint256", "name": "budget", "type": "uint256"}, {"internalType": "uint256", "name": "expiredAt", "type": "uint256"}, {"internalType": "enum AgenticCommerceEscrow.JobStatus", "name": "status", "type": "uint8"}], "stateMutability": "view", "type": "function"}, {"inputs": [], "name": "paymentToken", "outputs": [{"internalType": "contract IERC20", "name": "", "type": "address"}], "stateMutability": "view", "type": "function"}, {"inputs": [], "name": "platformFeeBps", "outputs": [{"internalType": "uint256", "name": "", "type": "uint256"}], "stateMutability": "view", "type": "function"}, {"inputs": [], "name": "platformTreasury", "outputs": [{"internalType": "address", "name": "", "type": "address"}], "stateMutability": "view", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "bytes32", "name": "reason", "type": "bytes32"}, {"internalType": "bytes", "name": "", "type": "bytes"}], "name": "reject", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "uint256", "name": "amount", "type": "uint256"}, {"internalType": "bytes", "name": "", "type": "bytes"}], "name": "setBudget", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "address", "name": "provider_", "type": "address"}], "name": "setProvider", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [{"internalType": "uint256", "name": "jobId", "type": "uint256"}, {"internalType": "bytes32", "name": "deliverable", "type": "bytes32"}, {"internalType": "bytes", "name": "", "type": "bytes"}], "name": "submit", "outputs": [], "stateMutability": "nonpayable", "type": "function"}, {"inputs": [], "name": "trustedForwarder", "outputs": [{"internalType": "address", "name": "", "type": "address"}], "stateMutability": "view", "type": "function"}];
export const ERC20_ABI = [{"constant": false, "inputs": [{"name": "_spender", "type": "address"}, {"name": "_value", "type": "uint256"}], "name": "approve", "outputs": [{"name": "", "type": "bool"}], "payable": false, "stateMutability": "nonpayable", "type": "function"}, {"constant": true, "inputs": [{"name": "_owner", "type": "address"}, {"name": "_spender", "type": "address"}], "name": "allowance", "outputs": [{"name": "", "type": "uint256"}], "payable": false, "stateMutability": "view", "type": "function"}];

import { resolveMetaMaskProvider } from "./coffeeStaking.js";

async function getProvider() {
    const provider = await resolveMetaMaskProvider();
    if (!provider) throw new Error("MetaMask not found");
    return provider;
}

async function getWeb3() {
    const provider = await getProvider();
    return new window.Web3(provider);
}

export async function createJob(contractAddress, providerAddress, evaluatorAddress, description) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    // expire in 30 days
    const expiredAt = Math.floor(Date.now() / 1000) + 30 * 24 * 60 * 60;
    
    return await contract.methods.createJob(providerAddress, evaluatorAddress, expiredAt, description).send({ from });
}

export async function setBudget(contractAddress, jobId, budgetEther) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    const budgetWei = web3.utils.toWei(budgetEther.toString(), 'ether');
    return await contract.methods.setBudget(jobId, budgetWei, "0x").send({ from });
}

export async function fundJob(contractAddress, paymentTokenAddress, jobId, budgetEther) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    
    const token = new web3.eth.Contract(ERC20_ABI, paymentTokenAddress);
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    const budgetWei = web3.utils.toWei(budgetEther.toString(), 'ether');
    
    // Check allowance
    const allowance = await token.methods.allowance(from, contractAddress).call();
    if (web3.utils.toBN(allowance).lt(web3.utils.toBN(budgetWei))) {
        // max uint256 is commonly used for max approval
        await token.methods.approve(contractAddress, web3.utils.toTwosComplement("-1")).send({ from });
    }
    
    return await contract.methods.fund(jobId, budgetWei, "0x").send({ from });
}

export async function submitEvidence(contractAddress, jobId, evidenceText) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    // convert text to bytes32, padding to 32 bytes
    const hex = web3.utils.utf8ToHex(evidenceText);
    const padded = web3.utils.padRight(hex, 64);
    
    return await contract.methods.submit(jobId, padded, "0x").send({ from });
}

export async function completeJob(contractAddress, jobId) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    return await contract.methods.complete(jobId, "0x0000000000000000000000000000000000000000000000000000000000000000", "0x").send({ from });
}

export async function rejectJob(contractAddress, jobId) {
    const provider = await getProvider();
    const web3 = await getWeb3();
    const accounts = await provider.request({ method: 'eth_requestAccounts' });
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    return await contract.methods.reject(jobId, "0x0000000000000000000000000000000000000000000000000000000000000000", "0x").send({ from });
}
