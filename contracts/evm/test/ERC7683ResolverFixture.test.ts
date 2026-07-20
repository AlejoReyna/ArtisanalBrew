import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { network } from "hardhat";
import { parseEther, zeroAddress, keccak256, encodeAbiParameters } from "viem";

describe("ERC7683ResolverFixture", async () => {
  const { viem } = await network.connect();
  const publicClient = await viem.getPublicClient();
  const [owner, user, solver, receiver, other] = await viem.getWalletClients();

  const sourceToken = await viem.deployContract("TestCafeToken", [owner.account.address, parseEther("1000000")]);
  const destToken = await viem.deployContract("TestCafeToken", [owner.account.address, parseEther("1000000")]);
  const resolver = await viem.deployContract("ERC7683ResolverFixture");

  await owner.writeContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "mint", args: [user.account.address, parseEther("1000")] });
  await owner.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "mint", args: [solver.account.address, parseEther("1000")] });
  await user.writeContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "approve", args: [resolver.address, parseEther("1000")] });
  await solver.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "approve", args: [resolver.address, parseEther("1000")] });
  await owner.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "mint", args: [other.account.address, parseEther("1000")] });
  await other.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "approve", args: [resolver.address, parseEther("1000")] });

  const getFuture = async () => (await publicClient.getBlock()).timestamp + 3600n;

  it("completes a successful full flow", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("10"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("9"),
      deadline: await getFuture(),
      nonce: 1n,
      allowedSolver: solver.account.address
    };

    const beforeSrc = await publicClient.readContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "balanceOf", args: [resolver.address] });
    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });
    const afterSrc = await publicClient.readContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "balanceOf", args: [resolver.address] });
    assert.equal(afterSrc - beforeSrc, parseEther("10"));

    const beforeDest = await publicClient.readContract({ address: destToken.address, abi: destToken.abi, functionName: "balanceOf", args: [receiver.account.address] });
    await solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("10")] });
    const afterDest = await publicClient.readContract({ address: destToken.address, abi: destToken.abi, functionName: "balanceOf", args: [receiver.account.address] });
    assert.equal(afterDest - beforeDest, parseEther("10"));
  });

  it("reverts fill before submit", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("10"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("9"),
      deadline: await getFuture(),
      nonce: 2n,
      allowedSolver: solver.account.address
    };
    await assert.rejects(
      solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("10")] }),
      /Not submitted/
    );
  });

  it("handles expiry refund correctly", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("10"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("9"),
      deadline: (await publicClient.getBlock()).timestamp + 10n,
      nonce: 3n,
      allowedSolver: solver.account.address
    };
    
    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "refundIntent", args: [order] }),
      /Deadline not passed/
    );

    await (publicClient as any).request({ method: "evm_increaseTime", params: [20] });
    await (publicClient as any).request({ method: "evm_mine", params: [] });

    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "refundIntent", args: [order] });
    
    await assert.rejects(
      solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("10")] }),
      /Deadline passed/
    );
  });
  
  it("reverts unauthorized solver", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("10"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("9"),
      deadline: await getFuture(),
      nonce: 4n,
      allowedSolver: solver.account.address
    };
    
    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });

    await assert.rejects(
      other.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("10")] }),
      /Unauthorized solver/
    );
  });

  it("reverts duplicate submission", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 5n,
      allowedSolver: solver.account.address
    };

    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] }),
      /Already submitted/
    );
  });

  it("reverts fill after resolve (replay protection)", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 6n,
      allowedSolver: solver.account.address
    };

    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });
    await solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("5")] });

    // Attempt to fill again — must revert
    await assert.rejects(
      solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("5")] }),
      /Already resolved/
    );
  });

  it("reverts fill with insufficient output amount", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 7n,
      allowedSolver: solver.account.address
    };

    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });

    await assert.rejects(
      solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("3")] }),
      /Insufficient output/
    );
  });

  it("reverts when non-user tries to submit", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 8n,
      allowedSolver: solver.account.address
    };

    await assert.rejects(
      other.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] }),
      /Only user can submit/
    );
  });

    it("reverts refund of already resolved intent", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: (await publicClient.getBlock()).timestamp + 10n,
      nonce: 9n,
      allowedSolver: solver.account.address
    };

    await user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] });
    await solver.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "fillIntent", args: [order, parseEther("5")] });

    await (publicClient as any).request({ method: "evm_increaseTime", params: [20] });
    await (publicClient as any).request({ method: "evm_mine", params: [] });

    // Refund must fail because already resolved
    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "refundIntent", args: [order] }),
      /Already resolved/
    );
  });

  it("reverts with zero amount", async () => {
    const order = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: 0n,
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 10n,
      allowedSolver: solver.account.address
    };

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [order] }),
      /Zero amount/
    );
  });

  it("reverts with zero address inputs", async () => {
    const baseOrder = {
      user: user.account.address,
      sourceToken: sourceToken.address,
      amountIn: parseEther("5"),
      destinationChainId: 84532n,
      destinationToken: destToken.address,
      destinationReceiver: receiver.account.address,
      minAmountOut: parseEther("4"),
      deadline: await getFuture(),
      nonce: 11n,
      allowedSolver: solver.account.address
    };

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [{ ...baseOrder, sourceToken: zeroAddress }] }),
      /Zero source token/
    );

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [{ ...baseOrder, destinationToken: zeroAddress }] }),
      /Zero destination token/
    );

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [{ ...baseOrder, destinationReceiver: zeroAddress }] }),
      /Zero receiver/
    );

    await assert.rejects(
      user.writeContract({ address: resolver.address, abi: resolver.abi, functionName: "submitIntent", args: [{ ...baseOrder, destinationChainId: 0n }] }),
      /Zero chain ID/
    );
  });
});
