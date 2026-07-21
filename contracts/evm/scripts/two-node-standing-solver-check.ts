/**
 * Proves the STANDING solver (CrossChainSolverWorker, a real .NET BackgroundService — not this
 * script) autonomously fills an intent: this script deploys everything, submits intents, and then
 * only WATCHES the destination chain for evidence of a fill it did not itself perform. It never
 * calls fillIntent.
 *
 * Usage:
 *   npx hardhat node --port 8546 &
 *   npx hardhat node --port 8547 &
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/two-node-standing-solver-check.ts --deploy
 *     -> prints CrossChainSolver__* / Blockchain__Chains__* env vars; start the real
 *        ThisCafeteria.Worker process with them before continuing
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/two-node-standing-solver-check.ts --submit-good
 *     -> submits a policy-approved intent, polls the destination chain for a fill, does not fill it itself
 *   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/two-node-standing-solver-check.ts --submit-denied
 *     -> submits an intent using a token pair NOT on the solver's allowlist, confirms it stays unfilled
 */
import { writeFileSync, readFileSync, existsSync } from "node:fs";
import { network } from "hardhat";
import { encodeFunctionData, parseEther, zeroAddress, type Address, type Hex } from "viem";

const STATE_PATH = "/tmp/standing-solver-state.json";
const mode = process.argv[2];

const source = await network.connect("arbitrumLocal");
const destination = await network.connect("baseLocal");
const sourcePublic = await source.viem.getPublicClient();
const destPublic = await destination.viem.getPublicClient();

type State = {
  sourceToken: Address; sourceResolver: Address;
  destToken: Address; destResolver: Address; entryPoint: Address; factory: Address; escrow: Address;
  accountAddress: Address; solverAddress: Address; solverPrivateKey: string;
  disallowedToken: Address;
};

if (mode === "--deploy") {
  const [sourceUser] = await source.viem.getWalletClients();
  const [destAdmin, destSolver, destProvider, destEvaluator] = await destination.viem.getWalletClients();

  const sourceToken = await source.viem.deployContract("TestCafeToken", [sourceUser.account.address, parseEther("1000")]);
  await sourceUser.writeContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "mint", args: [sourceUser.account.address, parseEther("1000")] });
  const sourceResolver = await source.viem.deployContract("ERC7683ResolverFixture");
  await sourceUser.writeContract({ address: sourceToken.address, abi: sourceToken.abi, functionName: "approve", args: [sourceResolver.address, parseEther("1000")] });

  const destToken = await destination.viem.deployContract("TestCafeToken", [destAdmin.account.address, parseEther("1000")]);
  await destAdmin.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "mint", args: [destAdmin.account.address, parseEther("1000")] });
  const disallowedToken = await destination.viem.deployContract("TestCafeToken", [destAdmin.account.address, parseEther("1000")]);
  const destResolver = await destination.viem.deployContract("ERC7683DestinationResolverFixture");
  const entryPoint = await destination.viem.deployContract("EntryPointFixture");
  const factory = await destination.viem.deployContract("CanonicalSimpleAccountFactory", [entryPoint.address]);
  const escrow = await destination.viem.deployContract("AgenticCommerceEscrow", [destToken.address, destAdmin.account.address, 100n, zeroAddress]);

  await destAdmin.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "transfer", args: [destSolver.account.address, parseEther("500")] });
  await destSolver.writeContract({ address: destToken.address, abi: destToken.abi, functionName: "approve", args: [destResolver.address, parseEther("500")] });

  const SALT = 0n;
  const accountAddress = (await destPublic.readContract({
    address: factory.address, abi: factory.abi, functionName: "getAddress", args: [destAdmin.account.address, SALT]
  })) as Address;
  await destAdmin.writeContract({ address: factory.address, abi: factory.abi, functionName: "createAccount", args: [destAdmin.account.address, SALT] });

  // solverPrivateKey: Hardhat's well-known account #1 key (published in Hardhat's own docs,
  // controls nothing outside a local node), matching destSolver (account index 1 on baseLocal).
  const state: State = {
    sourceToken: sourceToken.address, sourceResolver: sourceResolver.address,
    destToken: destToken.address, destResolver: destResolver.address, entryPoint: entryPoint.address,
    factory: factory.address, escrow: escrow.address, accountAddress,
    solverAddress: destSolver.account.address,
    solverPrivateKey: "0x59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d",
    disallowedToken: disallowedToken.address
  };
  writeFileSync(STATE_PATH, JSON.stringify(state, null, 2));

  console.log("Deployed. Start the real ThisCafeteria.Worker process with:\n");
  console.log(`Blockchain__DefaultChainKey=arbitrumLocal \\`);
  console.log(`Blockchain__Chains__0__Key=arbitrumLocal Blockchain__Chains__0__Family=Evm Blockchain__Chains__0__EvmChainId=421614 Blockchain__Chains__0__EvmChainIdHex=0x66eee Blockchain__Chains__0__PublicRpcUrl=http://127.0.0.1:8546 Blockchain__Chains__0__Enabled=true \\`);
  console.log(`Blockchain__Chains__1__Key=baseLocal Blockchain__Chains__1__Family=Evm Blockchain__Chains__1__EvmChainId=84532 Blockchain__Chains__1__EvmChainIdHex=0x14a34 Blockchain__Chains__1__PublicRpcUrl=http://127.0.0.1:8547 Blockchain__Chains__1__Enabled=true \\`);
  console.log(`CrossChainSolver__Enabled=true \\`);
  console.log(`CrossChainSolver__SourceChainKey=arbitrumLocal CrossChainSolver__SourceResolverAddress=${sourceResolver.address} \\`);
  console.log(`CrossChainSolver__DestinationChainKey=baseLocal CrossChainSolver__DestinationResolverAddress=${destResolver.address} \\`);
  console.log(`CrossChainSolver__SolverPrivateKey=${state.solverPrivateKey} \\`);
  console.log(`CrossChainSolver__AllowedTokenPairs__0__SourceToken=${sourceToken.address} CrossChainSolver__AllowedTokenPairs__0__DestinationToken=${destToken.address} \\`);
  console.log(`CrossChainSolver__MaxAmountIn=1000 CrossChainSolver__MaxOutputBps=10000 \\`);
  console.log(`dotnet run --project src/ThisCafeteria.Worker --environment Development`);
  process.exit(0);
}

if (!existsSync(STATE_PATH)) throw new Error("Run with --deploy first.");
const state = JSON.parse(readFileSync(STATE_PATH, "utf8")) as State;

const [sourceUser] = await source.viem.getWalletClients();
const executeAbi = [{
  type: "function", name: "execute",
  inputs: [{ name: "dest", type: "address" }, { name: "value", type: "uint256" }, { name: "func", type: "bytes" }],
  outputs: [], stateMutability: "nonpayable"
}] as const;
const resolverAbi = (await source.viem.getContractAt("ERC7683ResolverFixture", state.sourceResolver)).abi;
const destResolverAbi = (await destination.viem.getContractAt("ERC7683DestinationResolverFixture", state.destResolver)).abi;

async function submitAndWatch(sourceToken: Address, destToken: Address, nonce: bigint, expectFill: boolean) {
  const order = {
    user: sourceUser.account.address,
    sourceToken,
    amountIn: parseEther("10"),
    destinationChainId: 84532n,
    destinationToken: destToken,
    destinationReceiver: state.accountAddress,
    minAmountOut: parseEther("10"),
    deadline: (await sourcePublic.getBlock()).timestamp + 3600n,
    nonce,
    allowedSolver: zeroAddress // any solver may fill
  };

  const tx = await sourceUser.writeContract({ address: state.sourceResolver, abi: resolverAbi, functionName: "submitIntent", args: [order] });
  await sourcePublic.waitForTransactionReceipt({ hash: tx });
  const orderId = (await sourcePublic.readContract({ address: state.sourceResolver, abi: resolverAbi, functionName: "getOrderId", args: [order] })) as Hex;
  console.log(`submitted intent, orderId=${orderId}, waiting up to 30s for the STANDING WORKER to act (this script performs no fill)...`);

  const balanceBefore = await destPublic.readContract({ address: destToken, abi: (await destination.viem.getContractAt("TestCafeToken", destToken)).abi, functionName: "balanceOf", args: [state.accountAddress] }) as bigint;

  let resolved = false;
  for (let i = 0; i < 30 && !resolved; i++) {
    await new Promise((r) => setTimeout(r, 1000));
    resolved = (await destPublic.readContract({ address: state.destResolver, abi: destResolverAbi, functionName: "isResolved", args: [orderId] })) as boolean;
  }

  const balanceAfter = await destPublic.readContract({ address: destToken, abi: (await destination.viem.getContractAt("TestCafeToken", destToken)).abi, functionName: "balanceOf", args: [state.accountAddress] }) as bigint;

  if (expectFill) {
    if (!resolved) throw new Error(`FAIL: worker never filled orderId ${orderId} within 30s`);
    if (balanceAfter - balanceBefore !== order.amountIn) throw new Error("FAIL: destination account balance did not increase by the filled amount");
    console.log(`PASS: standing worker filled ${orderId} autonomously — balance +${balanceAfter - balanceBefore}`);
  } else {
    if (resolved) throw new Error(`FAIL: worker filled an intent (${orderId}) using a token pair that should have been denied`);
    if (balanceAfter !== balanceBefore) throw new Error("FAIL: balance changed despite the intent being outside the solver's policy");
    console.log(`PASS: standing worker correctly left ${orderId} unfilled (disallowed token pair)`);
  }
}

if (mode === "--submit-good") {
  await submitAndWatch(state.sourceToken, state.destToken, BigInt(process.argv[3] ?? "1"), true);
  console.log("STANDING_SOLVER_RESULT=PASS");
} else if (mode === "--submit-denied") {
  await submitAndWatch(state.sourceToken, state.disallowedToken, BigInt(process.argv[3] ?? "2"), false);
  console.log("STANDING_SOLVER_RESULT=PASS");
} else {
  throw new Error("Usage: --deploy | --submit-good | --submit-denied");
}
