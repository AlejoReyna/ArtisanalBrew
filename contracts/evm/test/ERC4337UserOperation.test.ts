import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { network } from "hardhat";
import {
  concat,
  encodeFunctionData,
  encodePacked,
  getContractAddress,
  pad,
  parseEther,
  toHex,
  zeroAddress,
  type Address,
  type Hex
} from "viem";

/**
 * Exercises the pinned canonical ERC-4337 v0.7.0 stack end to end: a UserOperation is built,
 * signed by the account owner, and executed through the canonical EntryPoint's handleOps.
 *
 * IMPORTANT BOUNDARY: these tests submit operations by calling EntryPoint.handleOps directly from
 * a funded EOA acting as the beneficiary. That is NOT a bundler — there is no mempool, no
 * eth_sendUserOperation, no bundler validation rules, and no gas/reputation policy. It proves the
 * on-chain half of ERC-4337 (account deployment via initCode, signature validation, execution,
 * prefund accounting), which is the part this repository actually deploys. A real bundler remains
 * an unmet Phase 4 dependency.
 */
describe("ERC-4337 canonical stack (UserOperation execution)", async () => {
  const { viem } = await network.connect();
  const publicClient = await viem.getPublicClient();
  const [owner, beneficiary, recipient] = await viem.getWalletClients();

  const entryPoint = await viem.deployContract("EntryPointFixture");
  const factory = await viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint.address]);

  const SALT = 0n;

  /** Pack two 128-bit values into one bytes32, as PackedUserOperation requires. */
  const packUint = (high: bigint, low: bigint): Hex =>
    encodePacked(["uint128", "uint128"], [high, low]);

  async function counterfactualAddress(ownerAddress: Address): Promise<Address> {
    return publicClient.readContract({
      address: factory.address,
      abi: factory.abi,
      functionName: "getAddress",
      args: [ownerAddress, SALT]
    }) as Promise<Address>;
  }

  function buildInitCode(ownerAddress: Address): Hex {
    return concat([
      factory.address,
      encodeFunctionData({
        abi: factory.abi,
        functionName: "createAccount",
        args: [ownerAddress, SALT]
      })
    ]);
  }

  /** SimpleAccount.execute(dest, value, func) */
  function buildCallData(dest: Address, value: bigint, func: Hex): Hex {
    return encodeFunctionData({
      abi: [
        {
          type: "function",
          name: "execute",
          inputs: [
            { name: "dest", type: "address" },
            { name: "value", type: "uint256" },
            { name: "func", type: "bytes" }
          ],
          outputs: [],
          stateMutability: "nonpayable"
        }
      ],
      functionName: "execute",
      args: [dest, value, func]
    });
  }

  type PackedUserOperation = {
    sender: Address;
    nonce: bigint;
    initCode: Hex;
    callData: Hex;
    accountGasLimits: Hex;
    preVerificationGas: bigint;
    gasFees: Hex;
    paymasterAndData: Hex;
    signature: Hex;
  };

  async function buildSignedOp(params: {
    sender: Address;
    initCode: Hex;
    callData: Hex;
    paymasterAndData?: Hex;
  }): Promise<PackedUserOperation> {
    const nonce = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getNonce",
      args: [params.sender, 0n]
    })) as bigint;

    const op: PackedUserOperation = {
      sender: params.sender,
      nonce,
      initCode: params.initCode,
      callData: params.callData,
      // verificationGasLimit | callGasLimit
      accountGasLimits: packUint(1_000_000n, 1_000_000n),
      preVerificationGas: 100_000n,
      // maxPriorityFeePerGas | maxFeePerGas
      gasFees: packUint(1_000_000_000n, 10_000_000_000n),
      paymasterAndData: params.paymasterAndData ?? "0x",
      signature: "0x"
    };

    // Let the canonical EntryPoint compute the hash rather than reimplementing it.
    const userOpHash = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getUserOpHash",
      args: [op]
    })) as Hex;

    // SimpleAccount validates an EIP-191 personal_sign over the userOpHash.
    const signature = await owner.signMessage({ message: { raw: userOpHash } });
    return { ...op, signature };
  }

  it("deploys the account via initCode and executes the call (user-paid via deposit)", async () => {
    const sender = await counterfactualAddress(owner.account.address);

    // Nothing deployed at the counterfactual address yet.
    assert.equal(await publicClient.getCode({ address: sender }), undefined);

    // The account pays for its own operation from its EntryPoint deposit.
    await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "depositTo",
      args: [sender],
      value: parseEther("1")
    });

    const before = await publicClient.getBalance({ address: recipient.account.address });
    const transferValue = parseEther("0.01");

    // Give the account ETH so it can forward value in the executed call.
    await beneficiary.sendTransaction({ to: sender, value: parseEther("0.1") });

    const op = await buildSignedOp({
      sender,
      initCode: buildInitCode(owner.account.address),
      callData: buildCallData(recipient.account.address, transferValue, "0x")
    });

    const hash = await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "handleOps",
      args: [[op], beneficiary.account.address],
      // Explicit gas: automatic estimation for handleOps exceeds the local block cap.
      gas: 3_000_000n
    });
    const receipt = await publicClient.waitForTransactionReceipt({ hash });
    assert.equal(receipt.status, "success", "handleOps should succeed");

    // Account was deployed by initCode.
    const code = await publicClient.getCode({ address: sender });
    assert.ok(code && code !== "0x", "account should be deployed after the first UserOperation");

    // The inner call actually executed.
    const after = await publicClient.getBalance({ address: recipient.account.address });
    assert.equal(after - before, transferValue, "recipient should have received the transferred value");
  });

  it("sponsors a UserOperation through the canonical VerifyingPaymaster", async () => {
    // A distinct owner so this account is independent of the user-paid test.
    const sponsoredOwner = (await viem.getWalletClients())[3];
    const verifyingSigner = (await viem.getWalletClients())[4];

    const paymaster = await viem.deployContract("CanonicalVerifyingPaymaster", [
      entryPoint.address,
      verifyingSigner.account.address
    ]);

    // The paymaster, not the account, funds the operation.
    await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "depositTo",
      args: [paymaster.address],
      value: parseEther("1")
    });

    const sender = await counterfactualAddress(sponsoredOwner.account.address);
    assert.equal(await publicClient.getCode({ address: sender }), undefined, "account starts undeployed");

    const nonce = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getNonce",
      args: [sender, 0n]
    })) as bigint;

    const validUntil = 0xffffffffn; // far future
    const validAfter = 0n;

    // paymasterAndData layout (v0.7): paymaster(20) | verificationGasLimit(16) |
    // postOpGasLimit(16) | abi.encode(validUntil, validAfter) | signature
    const timeRange = concat([pad(toHex(validUntil), { size: 32 }), pad(toHex(validAfter), { size: 32 })]);
    const paymasterStub = concat([
      paymaster.address,
      pad(toHex(500_000n), { size: 16 }),
      pad(toHex(200_000n), { size: 16 }),
      timeRange
    ]);

    const op: PackedUserOperation = {
      sender,
      nonce,
      initCode: buildInitCode(sponsoredOwner.account.address),
      callData: buildCallData(recipient.account.address, 0n, "0x"),
      accountGasLimits: packUint(1_000_000n, 1_000_000n),
      preVerificationGas: 100_000n,
      gasFees: packUint(1_000_000_000n, 10_000_000_000n),
      paymasterAndData: paymasterStub,
      signature: "0x"
    };

    // Let the paymaster compute the hash it will verify, then sign it off-chain.
    const sponsorHash = (await publicClient.readContract({
      address: paymaster.address,
      abi: paymaster.abi,
      functionName: "getHash",
      args: [op, Number(validUntil), Number(validAfter)]
    })) as Hex;

    const sponsorSignature = await verifyingSigner.signMessage({ message: { raw: sponsorHash } });
    op.paymasterAndData = concat([paymasterStub, sponsorSignature]);

    // The account owner still signs the operation itself.
    const userOpHash = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getUserOpHash",
      args: [op]
    })) as Hex;
    op.signature = await sponsoredOwner.signMessage({ message: { raw: userOpHash } });

    const paymasterDepositBefore = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "balanceOf",
      args: [paymaster.address]
    })) as bigint;

    const hash = await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "handleOps",
      args: [[op], beneficiary.account.address],
      gas: 3_000_000n
    });
    const receipt = await publicClient.waitForTransactionReceipt({ hash });
    assert.equal(receipt.status, "success", "sponsored handleOps should succeed");

    // Account deployed without the account itself ever holding a deposit.
    const code = await publicClient.getCode({ address: sender });
    assert.ok(code && code !== "0x", "sponsored account should be deployed");

    const senderDeposit = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "balanceOf",
      args: [sender]
    })) as bigint;
    assert.equal(senderDeposit, 0n, "the account must not have paid - the paymaster sponsors it");

    const paymasterDepositAfter = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "balanceOf",
      args: [paymaster.address]
    })) as bigint;
    assert.ok(paymasterDepositAfter < paymasterDepositBefore, "the paymaster deposit should have paid for gas");
  });

  it("rejects a sponsored UserOperation whose paymaster signature is from the wrong signer", async () => {
    const sponsoredOwner = (await viem.getWalletClients())[5];
    const verifyingSigner = (await viem.getWalletClients())[4];

    const paymaster = await viem.deployContract("CanonicalVerifyingPaymaster", [
      entryPoint.address,
      verifyingSigner.account.address
    ]);

    await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "depositTo",
      args: [paymaster.address],
      value: parseEther("1")
    });

    const sender = await counterfactualAddress(sponsoredOwner.account.address);
    const nonce = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getNonce",
      args: [sender, 0n]
    })) as bigint;

    const validUntil = 0xffffffffn;
    const validAfter = 0n;
    const timeRange = concat([pad(toHex(validUntil), { size: 32 }), pad(toHex(validAfter), { size: 32 })]);
    const paymasterStub = concat([
      paymaster.address,
      pad(toHex(500_000n), { size: 16 }),
      pad(toHex(200_000n), { size: 16 }),
      timeRange
    ]);

    const op: PackedUserOperation = {
      sender,
      nonce,
      initCode: buildInitCode(sponsoredOwner.account.address),
      callData: buildCallData(recipient.account.address, 0n, "0x"),
      accountGasLimits: packUint(1_000_000n, 1_000_000n),
      preVerificationGas: 100_000n,
      gasFees: packUint(1_000_000_000n, 10_000_000_000n),
      paymasterAndData: paymasterStub,
      signature: "0x"
    };

    const sponsorHash = (await publicClient.readContract({
      address: paymaster.address,
      abi: paymaster.abi,
      functionName: "getHash",
      args: [op, Number(validUntil), Number(validAfter)]
    })) as Hex;

    // Signed by the account owner instead of the paymaster's verifyingSigner.
    const badSponsorSignature = await sponsoredOwner.signMessage({ message: { raw: sponsorHash } });
    op.paymasterAndData = concat([paymasterStub, badSponsorSignature]);

    const userOpHash = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getUserOpHash",
      args: [op]
    })) as Hex;
    op.signature = await sponsoredOwner.signMessage({ message: { raw: userOpHash } });

    await assert.rejects(
      beneficiary.writeContract({
        address: entryPoint.address,
        abi: entryPoint.abi,
        functionName: "handleOps",
        args: [[op], beneficiary.account.address],
        gas: 3_000_000n
      }),
      "an unauthorised sponsorship signature must not be accepted"
    );
  });

  it("rejects a UserOperation signed by the wrong key", async () => {
    const wrongOwner = beneficiary;
    const sender = await counterfactualAddress(wrongOwner.account.address);

    await beneficiary.writeContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "depositTo",
      args: [sender],
      value: parseEther("1")
    });

    const nonce = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getNonce",
      args: [sender, 0n]
    })) as bigint;

    const op: PackedUserOperation = {
      sender,
      nonce,
      initCode: buildInitCode(wrongOwner.account.address),
      callData: buildCallData(recipient.account.address, 0n, "0x"),
      accountGasLimits: packUint(1_000_000n, 1_000_000n),
      preVerificationGas: 100_000n,
      gasFees: packUint(1_000_000_000n, 10_000_000_000n),
      paymasterAndData: "0x",
      signature: "0x"
    };

    const userOpHash = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getUserOpHash",
      args: [op]
    })) as Hex;

    // Signed by `owner`, but the account's owner is `wrongOwner`.
    const signature = await owner.signMessage({ message: { raw: userOpHash } });

    await assert.rejects(
      beneficiary.writeContract({
        address: entryPoint.address,
        abi: entryPoint.abi,
        functionName: "handleOps",
        args: [[{ ...op, signature }], beneficiary.account.address],
        // Explicit gas: estimation on a reverting operation overshoots the block cap.
        gas: 2_000_000n
      }),
      "EntryPoint must reject a UserOperation whose signature does not match the account owner"
    );
  });

  it("rejects a UserOperation with no prefund and no paymaster", async () => {
    // A fresh owner with no deposit: validation must fail rather than execute for free.
    const unfundedOwner = recipient;
    const sender = await counterfactualAddress(unfundedOwner.account.address);

    const nonce = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getNonce",
      args: [sender, 0n]
    })) as bigint;

    const op: PackedUserOperation = {
      sender,
      nonce,
      initCode: buildInitCode(unfundedOwner.account.address),
      callData: buildCallData(zeroAddress, 0n, "0x"),
      accountGasLimits: packUint(1_000_000n, 1_000_000n),
      preVerificationGas: 100_000n,
      gasFees: packUint(1_000_000_000n, 10_000_000_000n),
      paymasterAndData: "0x",
      signature: "0x"
    };

    const userOpHash = (await publicClient.readContract({
      address: entryPoint.address,
      abi: entryPoint.abi,
      functionName: "getUserOpHash",
      args: [op]
    })) as Hex;

    const signature = await unfundedOwner.signMessage({ message: { raw: userOpHash } });

    await assert.rejects(
      beneficiary.writeContract({
        address: entryPoint.address,
        abi: entryPoint.abi,
        functionName: "handleOps",
        args: [[{ ...op, signature }], beneficiary.account.address]
      }),
      "EntryPoint must reject an operation that cannot pay its prefund"
    );
  });
});
