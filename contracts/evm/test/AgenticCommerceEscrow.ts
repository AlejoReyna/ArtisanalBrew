import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { network } from "hardhat";
import { keccak256, parseEther, zeroAddress } from "viem";

describe("AgenticCommerceEscrow", async () => {
  const { viem } = await network.connect();
  const publicClient = await viem.getPublicClient();
  const [client, provider, evaluator, treasury, other] = await viem.getWalletClients();
  const token = await viem.deployContract("TestCafeToken", [client.account.address, parseEther("1000000")]);
  const escrow = await viem.deployContract("AgenticCommerceEscrow", [token.address, treasury.account.address, 100n, zeroAddress]);
  const future = async () => (await publicClient.getBlock()).timestamp + 3600n;

  await client.writeContract({ address: token.address, abi: token.abi, functionName: "mint", args: [client.account.address, parseEther("1000")] });

  async function job() {
    const hash = await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "createJob", args: [provider.account.address, evaluator.account.address, await future(), "proposal-v1"] });
    const receipt = await publicClient.waitForTransactionReceipt({ hash });
    const logs = await publicClient.getContractEvents({ address: escrow.address, abi: escrow.abi, eventName: "JobCreated", fromBlock: receipt.blockNumber, toBlock: receipt.blockNumber });
    return logs[0].args.jobId!;
  }

  async function fundedJob() {
    const id = await job();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "setBudget", args: [id, parseEther("10"), "0x"] });
    await client.writeContract({ address: token.address, abi: token.abi, functionName: "approve", args: [escrow.address, parseEther("10")] });
    await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("10"), "0x"] });
    return id;
  }

  it("completes and pays the provider after evaluator submission", async () => {
    const id = await job();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "setBudget", args: [id, parseEther("100"), "0x"] });
    await client.writeContract({ address: token.address, abi: token.abi, functionName: "approve", args: [escrow.address, parseEther("100")] });
    await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("100"), "0x"] });
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "submit", args: [id, keccak256("0x02"), "0x"] });
    const before = await publicClient.readContract({ address: token.address, abi: token.abi, functionName: "balanceOf", args: [provider.account.address] });
    await evaluator.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "complete", args: [id, keccak256("0x03"), "0x"] });
    const after = await publicClient.readContract({ address: token.address, abi: token.abi, functionName: "balanceOf", args: [provider.account.address] });
    assert.equal(after - before, parseEther("99"));
  });

  it("rejects funded work and refunds the client", async () => {
    const id = await job();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "setBudget", args: [id, parseEther("10"), "0x"] });
    await client.writeContract({ address: token.address, abi: token.abi, functionName: "approve", args: [escrow.address, parseEther("10")] });
    await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("10"), "0x"] });
    const before = await publicClient.readContract({ address: token.address, abi: token.abi, functionName: "balanceOf", args: [client.account.address] });
    await evaluator.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "reject", args: [id, keccak256("0x04"), "0x"] });
    const after = await publicClient.readContract({ address: token.address, abi: token.abi, functionName: "balanceOf", args: [client.account.address] });
    assert.equal(after - before, parseEther("10"));
  });

  it("expires and refunds without an evaluator call", async () => {
    const id = await job();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "setBudget", args: [id, parseEther("5"), "0x"] });
    await client.writeContract({ address: token.address, abi: token.abi, functionName: "approve", args: [escrow.address, parseEther("5")] });
    await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("5"), "0x"] });
    await (publicClient as any).request({ method: "evm_increaseTime", params: [3601] });
    await (publicClient as any).request({ method: "evm_mine", params: [] });
    await client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "claimRefund", args: [id] });
    const stored = await publicClient.readContract({ address: escrow.address, abi: escrow.abi, functionName: "jobs", args: [id] });
    assert.equal(stored[7], 5); // JobStatus.Expired
  });

  // --- New deterministic tests ---

  it("reverts when unauthorized provider tries to submit", async () => {
    const id = await fundedJob();
    await assert.rejects(
      other.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "submit", args: [id, keccak256("0x05"), "0x"] }),
      /Unauthorized/
    );
  });

  it("reverts when unauthorized evaluator tries to complete", async () => {
    const id = await fundedJob();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "submit", args: [id, keccak256("0x06"), "0x"] });
    await assert.rejects(
      other.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "complete", args: [id, keccak256("0x07"), "0x"] }),
      /Unauthorized/
    );
  });

  it("reverts duplicate fund on already-funded job", async () => {
    const id = await fundedJob();
    await client.writeContract({ address: token.address, abi: token.abi, functionName: "approve", args: [escrow.address, parseEther("10")] });
    await assert.rejects(
      client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("10"), "0x"] }),
      /WrongStatus/
    );
  });

  it("reverts claim refund before expiry", async () => {
    const id = await fundedJob();
    await assert.rejects(
      client.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "claimRefund", args: [id] }),
      /WrongStatus/
    );
  });

  it("reverts when non-client tries to fund", async () => {
    const id = await job();
    await provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "setBudget", args: [id, parseEther("10"), "0x"] });
    await assert.rejects(
      other.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "fund", args: [id, parseEther("10"), "0x"] }),
      /Unauthorized/
    );
  });

  it("reverts submit on unfunded job", async () => {
    const id = await job();
    await assert.rejects(
      provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "submit", args: [id, keccak256("0x08"), "0x"] }),
      /WrongStatus/
    );
  });

  it("reverts complete on funded (not submitted) job", async () => {
    const id = await fundedJob();
    await assert.rejects(
      evaluator.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "complete", args: [id, keccak256("0x09"), "0x"] }),
      /WrongStatus/
    );
  });

  it("reverts operations on non-existent job", async () => {
    const fakeId = 99999n;
    await assert.rejects(
      provider.writeContract({ address: escrow.address, abi: escrow.abi, functionName: "submit", args: [fakeId, keccak256("0x0a"), "0x"] }),
      /InvalidJob/
    );
  });
});
