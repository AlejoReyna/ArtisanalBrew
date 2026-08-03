import json

abi_path = "contracts/evm/artifacts/contracts/AgenticCommerceEscrow.sol/AgenticCommerceEscrow.json"
erc20_abi = [
    {
        "constant": False,
        "inputs": [{"name": "_spender", "type": "address"}, {"name": "_value", "type": "uint256"}],
        "name": "approve",
        "outputs": [{"name": "", "type": "bool"}],
        "payable": False,
        "stateMutability": "nonpayable",
        "type": "function"
    },
    {
        "constant": True,
        "inputs": [{"name": "_owner", "type": "address"}, {"name": "_spender", "type": "address"}],
        "name": "allowance",
        "outputs": [{"name": "", "type": "uint256"}],
        "payable": False,
        "stateMutability": "view",
        "type": "function"
    }
]

with open(abi_path) as f:
    escrow_abi = json.load(f)["abi"]

js_code = f"""
export const ESCROW_ABI = {json.dumps(escrow_abi)};
export const ERC20_ABI = {json.dumps(erc20_abi)};

function getWeb3() {{
    if (!window.ethereum) throw new Error("MetaMask not found");
    return new window.Web3(window.ethereum);
}}

export async function createJob(contractAddress, providerAddress, evaluatorAddress, description) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    // expire in 30 days
    const expiredAt = Math.floor(Date.now() / 1000) + 30 * 24 * 60 * 60;
    
    return await contract.methods.createJob(providerAddress, evaluatorAddress, expiredAt, description).send({{ from }});
}}

export async function setBudget(contractAddress, jobId, budgetEther) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    const budgetWei = web3.utils.toWei(budgetEther.toString(), 'ether');
    return await contract.methods.setBudget(jobId, budgetWei, "0x").send({{ from }});
}}

export async function fundJob(contractAddress, paymentTokenAddress, jobId, budgetEther) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    
    const token = new web3.eth.Contract(ERC20_ABI, paymentTokenAddress);
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    const budgetWei = web3.utils.toWei(budgetEther.toString(), 'ether');
    
    // Check allowance
    const allowance = await token.methods.allowance(from, contractAddress).call();
    if (web3.utils.toBN(allowance).lt(web3.utils.toBN(budgetWei))) {{
        // max uint256 is commonly used for max approval
        await token.methods.approve(contractAddress, web3.utils.toTwosComplement("-1")).send({{ from }});
    }}
    
    return await contract.methods.fund(jobId, budgetWei, "0x").send({{ from }});
}}

export async function submitEvidence(contractAddress, jobId, evidenceText) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    // convert text to bytes32, padding to 32 bytes
    const hex = web3.utils.utf8ToHex(evidenceText);
    const padded = web3.utils.padRight(hex, 64);
    
    return await contract.methods.submit(jobId, padded, "0x").send({{ from }});
}}

export async function completeJob(contractAddress, jobId) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    return await contract.methods.complete(jobId, "0x0000000000000000000000000000000000000000000000000000000000000000", "0x").send({{ from }});
}}

export async function rejectJob(contractAddress, jobId) {{
    const web3 = getWeb3();
    const accounts = await window.ethereum.request({{ method: 'eth_requestAccounts' }});
    const from = accounts[0];
    const contract = new web3.eth.Contract(ESCROW_ABI, contractAddress);
    
    return await contract.methods.reject(jobId, "0x0000000000000000000000000000000000000000000000000000000000000000", "0x").send({{ from }});
}}
"""

with open("src/ThisCafeteria.Web/wwwroot/js/agenticCommerce.js", "w") as f:
    f.write(js_code)
