import assert from "node:assert/strict";
import { before, describe, it } from "node:test";
import { network } from "hardhat";
import { parseEther } from "viem";

describe("CafeLiquidStakingVault", async function () {
  const { viem } = await network.connect();
  const publicClient = await viem.getPublicClient();
  const [alice, bob, admin] = await viem.getWalletClients();
  const cafe = await viem.deployContract("TestCafeToken", [admin.account.address, parseEther("1000000")]);
  const coffee = await viem.deployContract("TestCoffeeToken", [admin.account.address, parseEther("1000000")]);
  const vault = await viem.deployContract("CafeLiquidStakingVault", [admin.account.address, cafe.address, coffee.address]);

  before(async () => {
    for (const wallet of [alice, bob]) {
      await admin.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "mint", args: [wallet.account.address, parseEther("1000")] });
      await wallet.writeContract({ address: cafe.address, abi: cafe.abi, functionName: "approve", args: [vault.address, parseEther("1000")] });
    }
    await admin.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "mint", args: [admin.account.address, parseEther("1000")] });
    await admin.writeContract({ address: coffee.address, abi: coffee.abi, functionName: "transfer", args: [vault.address, parseEther("1000")] });
    await admin.writeContract({ address: vault.address, abi: vault.abi, functionName: "notifyRewardAmount", args: [parseEther("1000"), 1000n] });
  });

  it("deposits, transfers liquid shares, and keeps prior accrual with the sender", async () => {
    await alice.writeContract({ address: vault.address, abi: vault.abi, functionName: "deposit", args: [parseEther("100"), alice.account.address] });
    const sharesBefore = await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "balanceOf", args: [alice.account.address] });
    assert.equal(sharesBefore, parseEther("100"));
    await (publicClient as any).request({ method: "evm_increaseTime", params: [100] });
    await (publicClient as any).request({ method: "evm_mine", params: [] });
    const accruedBeforeTransfer = await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "earned", args: [alice.account.address] });
    assert.ok(accruedBeforeTransfer > 0n);
    await alice.writeContract({ address: vault.address, abi: vault.abi, functionName: "transfer", args: [bob.account.address, parseEther("40")] });
    const aliceEarned = await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "earned", args: [alice.account.address] });
    const bobEarned = await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "earned", args: [bob.account.address] });
    assert.ok(aliceEarned > 0n);
    assert.ok(aliceEarned + bobEarned <= parseEther("1000"));
    assert.equal(await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "balanceOf", args: [bob.account.address] }), parseEther("40"));
  });

  it("allows redemption while deposits are paused", async () => {
    await admin.writeContract({ address: vault.address, abi: vault.abi, functionName: "pauseDeposits" });
    assert.equal(await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "maxDeposit", args: [alice.account.address] }), 0n);
    assert.equal(await publicClient.readContract({ address: vault.address, abi: vault.abi, functionName: "maxMint", args: [alice.account.address] }), 0n);
    await alice.writeContract({ address: vault.address, abi: vault.abi, functionName: "redeem", args: [parseEther("10"), alice.account.address, alice.account.address] });
    await admin.writeContract({ address: vault.address, abi: vault.abi, functionName: "unpauseDeposits" });
  });
});
