import { network } from "hardhat";
import { parseEther, toHex, padHex, getContract } from "viem";
import pkg from "pg";
const { Client } = pkg;
import { readFileSync } from "fs";

async function main() {
    const { viem } = await network.connect();
    const publicClient = await viem.getPublicClient();
    const wallets = await viem.getWalletClients();
    const clientWallet = wallets[0];
    const providerWallet = wallets[1];
    const evaluatorWallet = wallets[2];

    const manifest = JSON.parse(readFileSync("deployments/evm-local.json", "utf8"));
    const escrowAddress = manifest.addresses.erc8183Escrow;
    const tokenAddress = manifest.addresses.cafe;
    const registryAddress = manifest.addresses.erc8004Registry;

    const escrow = await viem.getContractAt("AgenticCommerceEscrow", escrowAddress);
    const token = await viem.getContractAt("TestCafeToken", tokenAddress);
    const registry = await viem.getContractAt("ERC8004RegistryFixture", registryAddress);

    console.log("Setting up database connection...");
    const pg = new Client({
        host: "localhost",
        port: 5433,
        database: "this_cafeteria",
        user: "postgres",
        password: "postgres"
    });
    await pg.connect();

    console.log("1. Registering Agent Identity...");
    const regTx = await registry.write.registerAgent(["https://agent.example.com"], { account: providerWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: regTx });

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgentDirectoryEntries" WHERE LOWER("OwnerAddress") = LOWER($1)', [providerWallet.account.address]);
        return res.rows.length > 0;
    });
    console.log("Agent Identity verified in DB.");

    console.log("2. Creating Job...");
    const description = "Test Job Proposal";
    const expireTime = BigInt(Math.floor(Date.now() / 1000) + 3600);
    const createTx = await escrow.write.createJob([providerWallet.account.address, evaluatorWallet.account.address, expireTime, description], { account: clientWallet.account });
    const createReceipt = await publicClient.waitForTransactionReceipt({ hash: createTx });
    
    // Parse logs to find jobId
    const createLogs = await publicClient.getLogs({
        address: escrowAddress,
        event: {
            type: "event",
            name: "JobCreated",
            inputs: [
                { indexed: true, name: "jobId", type: "uint256" },
                { indexed: true, name: "client", type: "address" },
                { indexed: true, name: "provider", type: "address" },
                { indexed: false, name: "evaluator", type: "address" },
                { indexed: false, name: "expiredAt", type: "uint256" }
            ]
        },
        fromBlock: createReceipt.blockNumber,
        toBlock: createReceipt.blockNumber
    });
    const jobId = createLogs[0].args.jobId;
    console.log(`Job created with ID: ${jobId}`);

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [jobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Open";
    });
    console.log("Job verified in DB.");

    console.log("3. Setting Budget...");
    const budget = parseEther("50");
    const budgetTx = await escrow.write.setBudget([jobId, budget, "0x"], { account: clientWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: budgetTx });

    console.log("4. ERC-20 Approval & Funding...");
    const approveTx = await token.write.approve([escrowAddress, budget], { account: clientWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: approveTx });

    const fundTx = await escrow.write.fund([jobId, budget, "0x"], { account: clientWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: fundTx });

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [jobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Funded";
    });
    console.log("Job Funding verified in DB.");

    console.log("5. Provider Submission...");
    const deliverable = padHex(toHex("ipfs://QmHash"), { size: 32 });
    const submitTx = await escrow.write.submit([jobId, deliverable, "0x"], { account: providerWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: submitTx });

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [jobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Submitted";
    });
    console.log("Job Submission verified in DB.");

    console.log("6. Evaluator Completion...");
    const reason = padHex(toHex("approved"), { size: 32 });
    
    const initialBalance = await token.read.balanceOf([providerWallet.account.address]);

    const completeTx = await escrow.write.complete([jobId, reason, "0x"], { account: evaluatorWallet.account });
    await publicClient.waitForTransactionReceipt({ hash: completeTx });

    const finalBalance = await token.read.balanceOf([providerWallet.account.address]);
    if (finalBalance <= initialBalance) throw new Error("Provider did not receive funds");
    console.log("Provider payout verified on-chain.");

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [jobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Completed";
    });
    console.log("Job Completion verified in DB.");

    console.log("=== Running Rejection Variant ===");
    const rejectJobTx = await escrow.write.createJob([providerWallet.account.address, evaluatorWallet.account.address, expireTime, description], { account: clientWallet.account });
    const rejectReceipt = await publicClient.waitForTransactionReceipt({ hash: rejectJobTx });
    const rejectLogs = await publicClient.getLogs({
        address: escrowAddress,
        event: { type: "event", name: "JobCreated", inputs: [{ indexed: true, name: "jobId", type: "uint256" }, { indexed: true, name: "client", type: "address" }, { indexed: true, name: "provider", type: "address" }, { indexed: false, name: "evaluator", type: "address" }, { indexed: false, name: "expiredAt", type: "uint256" }] },
        fromBlock: rejectReceipt.blockNumber,
        toBlock: rejectReceipt.blockNumber
    });
    const rJobId = rejectLogs[0].args.jobId;
    await escrow.write.setBudget([rJobId, budget, "0x"], { account: clientWallet.account });
    await token.write.approve([escrowAddress, budget], { account: clientWallet.account });
    await escrow.write.fund([rJobId, budget, "0x"], { account: clientWallet.account });
    await escrow.write.submit([rJobId, deliverable, "0x"], { account: providerWallet.account });
    
    const clientInitialBalance = await token.read.balanceOf([clientWallet.account.address]);
    await escrow.write.reject([rJobId, reason, "0x"], { account: evaluatorWallet.account });
    const clientFinalBalance = await token.read.balanceOf([clientWallet.account.address]);
    if (clientFinalBalance <= clientInitialBalance) throw new Error("Client did not receive refund after rejection");
    
    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [rJobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Rejected";
    });
    console.log("Rejection variant passed.");

    console.log("=== Running Expiry Variant ===");
    const expiryTime = BigInt(Math.floor(Date.now() / 1000) + 2); // 2 seconds from now
    const expiryJobTx = await escrow.write.createJob([providerWallet.account.address, evaluatorWallet.account.address, expiryTime, description], { account: clientWallet.account });
    const expiryReceipt = await publicClient.waitForTransactionReceipt({ hash: expiryJobTx });
    const expiryLogs = await publicClient.getLogs({
        address: escrowAddress,
        event: { type: "event", name: "JobCreated", inputs: [{ indexed: true, name: "jobId", type: "uint256" }, { indexed: true, name: "client", type: "address" }, { indexed: true, name: "provider", type: "address" }, { indexed: false, name: "evaluator", type: "address" }, { indexed: false, name: "expiredAt", type: "uint256" }] },
        fromBlock: expiryReceipt.blockNumber,
        toBlock: expiryReceipt.blockNumber
    });
    const eJobId = expiryLogs[0].args.jobId;
    await escrow.write.setBudget([eJobId, budget, "0x"], { account: clientWallet.account });
    await token.write.approve([escrowAddress, budget], { account: clientWallet.account });
    await escrow.write.fund([eJobId, budget, "0x"], { account: clientWallet.account });
    
    console.log("Waiting for expiry...");
    await sleep(3000); // Wait for block timestamp to pass expiry
    // Mine a block to update block timestamp
    await network.provider.send("evm_mine");

    const eClientInitialBalance = await token.read.balanceOf([clientWallet.account.address]);
    await escrow.write.claimRefund([eJobId], { account: clientWallet.account });
    const eClientFinalBalance = await token.read.balanceOf([clientWallet.account.address]);
    if (eClientFinalBalance <= eClientInitialBalance) throw new Error("Client did not receive refund after expiry");

    await waitForDB(publicClient, async () => {
        let res = await pg.query('SELECT * FROM "AgenticJobs" WHERE "OnChainJobId" = $1', [eJobId]);
        return res.rows.length > 0 && res.rows[0].Status === "Expired";
    });
    console.log("Expiry variant passed.");

    await pg.end();
    console.log("✅ Full lifecycle acceptance test passed successfully!");
    process.exit(0);
}

async function waitForDB(publicClient: any, checkFn: () => Promise<boolean>) {
    // Mine some blocks initially
    for(let i=0; i<3; i++) {
        await publicClient.request({ method: "evm_mine" });
    }
    
    // Poll up to 10 seconds
    for(let i=0; i<20; i++) {
        try {
            const ok = await checkFn();
            if (ok) return;
        } catch(e) {}
        await new Promise(resolve => setTimeout(resolve, 500));
        // Mine an extra block sometimes just in case
        if (i % 2 === 0) await publicClient.request({ method: "evm_mine" });
    }
    throw new Error("Timeout waiting for DB state");
}

main().catch(err => {
    console.error("Test failed:", err);
    process.exit(1);
});
