using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services;

// Cross-stack verification tool, NOT a unit test.
//
// contracts/evm/scripts/crossstack-sponsor-check.ts builds a real UserOperation against a live
// Hardhat node, writes its fields to a JSON file, and shells out to this program. This program
// then runs the REAL production classes (UserOperationSponsor, UserOperationSimulator,
// SponsorshipPolicyService) — not stubs — against that same live node: simulating real gas,
// pricing it, evaluating the sponsorship policy, and producing a paymaster signature.
//
// In "approve"/"wrongtarget" mode, the TypeScript side then submits that signature to the
// canonical on-chain EntryPoint directly (handleOps) and checks whether it's accepted — proving
// the signature in isolation.
//
// In "submit" mode (contracts/evm/scripts/crossstack-bundler-submit-check.ts), this program
// instead runs the REAL production UserOperationSubmitter — also not a stub — which submits the
// already-signed operation through a real bundler (RundlerBundlerClient, talking to a locally
// running Rundler process) via eth_sendUserOperation, polls eth_getUserOperationReceipt, and
// independently verifies the mined result by decoding the EntryPoint's own UserOperationEvent from
// the real transaction receipt. This is the first proof that closes the gap the stack plan
// documented: no ThisCafeteria.* code submitted through a bundler before this.
//
// The point of doing it this way, rather than testing everything in one language, is that a
// signature or a cost figure that only this codebase's own logic agrees with proves nothing — the
// real EntryPoint, paymaster, and (for "submit") bundler are the arbiters, and C# and Solidity/the
// bundler have to agree with THEM, not with each other.
//
// This is not wired into `dotnet test` because it requires a live Hardhat node with the canonical
// EntryPoint/factory/paymaster already deployed (and, for "submit", a running Rundler bundler).
// Run it via the Hardhat scripts:
//   HARDHAT_NETWORK=localhost npx tsx scripts/crossstack-sponsor-check.ts
//   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-bundler-submit-check.ts

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ThisCafeteria.CrossStackHarness <path-to-op-description.json> [approve|wrongtarget|submit]");
    return 1;
}

var opPath = args[0];
var mode = args.Length > 1 ? args[1] : "approve";
var doc = JsonDocument.Parse(File.ReadAllText(opPath)).RootElement;

string ReadString(string key) => doc.GetProperty(key).GetString()!;
System.Numerics.BigInteger ReadBigInteger(string key) => System.Numerics.BigInteger.Parse(doc.GetProperty(key).GetString()!);

// Hardhat's first well-known development account and its published private key. Neither is a
// secret — they control nothing outside a local test node — and are the default because most
// invocations of this harness never touch a real chain. A real network (e.g. Sepolia) overrides
// both via environment variables rather than a code change: the owner is whatever address the
// calling script derived the counterfactual account for, and the verifying-signer key is whatever
// key the real deployed VerifyingPaymaster actually trusts (deploy.ts uses the network's own
// deployer account for both roles — see docs/agentic-commerce-stack-plan.md's Sepolia handoff).
var Owner = Environment.GetEnvironmentVariable("CROSSSTACK_OWNER_ADDRESS") ?? "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
var DevSignerKey = Environment.GetEnvironmentVariable("CROSSSTACK_VERIFYING_SIGNER_KEY") ?? "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";

var target = ReadString("target");
var selector = ReadString("selector");

// "wrongtarget" mode proves a policy denial yields no signature at all, by pointing the
// allowlist at an address the operation does NOT call.
var allowedTarget = mode == "wrongtarget" ? "0x000000000000000000000000000000000000dead" : target;

var evmChainId = int.TryParse(Environment.GetEnvironmentVariable("CROSSSTACK_EVM_CHAIN_ID"), out var parsedChainId) ? parsedChainId : 31337;
var chain = new ChainDefinition
{
    Key = "evm-local",
    Family = ChainFamily.Evm,
    EvmChainId = evmChainId,
    EvmChainIdHex = $"0x{evmChainId:x}",
    // Set by the calling script (crossstack-sponsor-check.ts / crossstack-bundler-submit-check.ts)
    // to match whichever Hardhat network it connected to - this process has no Hardhat context of
    // its own. Defaults to 8545 for the README-documented manual `HARDHAT_NETWORK=localhost`
    // single-node flow.
    PublicRpcUrl = Environment.GetEnvironmentVariable("CROSSSTACK_RPC_URL") ?? "http://127.0.0.1:8545",
    // Only meaningful in "submit" mode; a running Rundler process the calling script started.
    BundlerRpcUrl = Environment.GetEnvironmentVariable("CROSSSTACK_BUNDLER_RPC_URL"),
    Deployment = new ChainDeployment
    {
        EntryPoint = ReadString("entryPoint"),
        AccountFactory = ReadString("accountFactory"),
        VerifyingPaymaster = ReadString("paymaster")
    }
};

var options = new SponsorshipPolicyOptions
{
    Enabled = true,
    AllowedTargets = [allowedTarget],
    AllowedSelectors = [selector],
    VerifyingSignerPrivateKey = DevSignerKey,
    NativeCurrencyUsdRate = 3000m
};

using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();
using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
db.Database.EnsureCreated();
db.SponsorshipGrants.Add(new SponsorshipGrant
{
    ChainKey = "evm-local",
    OwnerAddress = Owner,
    BudgetUsd = 100m,
    SpentUsd = 0m,
    MaxOperationCostUsd = 0m,
    ValidFromUtc = DateTime.UtcNow.AddDays(-1),
    ValidUntilUtc = DateTime.UtcNow.AddDays(1)
});
db.SaveChanges();

var registry = new SingleChainRegistry(chain);
var policy = new SponsorshipPolicyService(db, registry, options, TimeProvider.System, NullLogger<SponsorshipPolicyService>.Instance);

// The real UserOperationSimulator — the same class the production Web app registers via DI —
// running the same eth_call state-override recipe against the live Hardhat node.
var simulator = new UserOperationSimulator(registry, NullLogger<UserOperationSimulator>.Instance);
var sponsor = new UserOperationSponsor(registry, policy, simulator, options, TimeProvider.System, NullLogger<UserOperationSponsor>.Instance);

var operation = new SponsoredUserOperation
{
    ChainKey = "evm-local",
    OwnerAddress = Owner,
    Sender = ReadString("sender"),
    Nonce = ReadBigInteger("nonce"),
    InitCode = ReadString("initCode"),
    CallData = ReadString("callData"),
    AccountGasLimits = ReadString("accountGasLimits"),
    PreVerificationGas = ReadBigInteger("preVerificationGas"),
    GasFees = ReadString("gasFees"),
    TargetAddress = target,
    Selector = selector
};

if (mode == "submit")
{
    // The caller (crossstack-bundler-submit-check.ts) already ran this same harness in "approve"
    // mode to obtain paymasterAndData, computed EntryPoint.getUserOpHash over it, and signed that
    // hash with the owner's key - exactly like crossstack-sponsor-check.ts does before calling
    // handleOps directly. The difference here is what happens next: instead of the TypeScript side
    // submitting to the EntryPoint itself, this process runs the REAL production
    // UserOperationSubmitter, which submits through a real bundler (Rundler, via
    // RundlerBundlerClient) and independently verifies the mined result on-chain.
    //
    // Short timeout: a "denied" proof (see below) points the bundler URL at an address nothing
    // listens on, specifically so a bug that skipped the approval check would surface as a fast,
    // unambiguous connection failure rather than hanging.
    using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var bundlerClient = new RundlerBundlerClient(httpClient, registry);
    var submitter = new UserOperationSubmitter(registry, bundlerClient, policy, TimeProvider.System, NullLogger<UserOperationSubmitter>.Instance);

    // Negative-path proof (crossstack-bundler-submit-denied-check.ts): an op JSON with
    // `"denied": true` builds an unapproved SponsorshipSignature instead of reading one from JSON,
    // proving through this exact production code path - not a mocked unit test - that a denied
    // sponsorship never reaches the bundler at all. Paired with a deliberately unreachable
    // BundlerRpcUrl, any attempt to contact it surfaces as a fast, loud failure instead of silently
    // passing.
    var denied = doc.TryGetProperty("denied", out var deniedElement) && deniedElement.GetBoolean();
    var sponsorship = denied
        ? SponsorshipSignature.Deny(SponsorshipDenialReason.DisallowedTarget, "denied for cross-stack negative-path proof")
        : new SponsorshipSignature
        {
            Approved = true,
            Reason = SponsorshipDenialReason.None,
            PaymasterAndData = ReadString("paymasterAndData"),
            CostUsd = doc.GetProperty("costUsd").GetDecimal()
        };

    var submission = await submitter.SubmitAsync(operation, sponsorship, denied ? "0xdeadbeef" : ReadString("signature"));

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = submission.Status.ToString(),
        detail = submission.Detail,
        userOpHash = submission.UserOperationHash,
        transactionHash = submission.TransactionHash,
        costUsd = submission.CostUsd
    }));

    // Consistent with "approve"/"wrongtarget" mode below: this process's job is to report the real
    // outcome, not to decide whether that outcome is the one the calling script wanted. A denial
    // is the CORRECT result for crossstack-bundler-submit-denied-check.ts and would be the wrong
    // one for crossstack-bundler-submit-check.ts - only the caller knows which. Always exit 0 here
    // and let the caller inspect `status` itself, the same as it already does for `approved`.
    return 0;
}

var result = await sponsor.SponsorAsync(operation);

Console.WriteLine(JsonSerializer.Serialize(new
{
    approved = result.Approved,
    reason = result.Reason.ToString(),
    detail = result.Detail,
    costUsd = result.CostUsd,
    paymasterAndData = result.PaymasterAndData
}));

return 0;

sealed class SingleChainRegistry(ChainDefinition chain) : IChainRegistry
{
    public string DefaultChainKey => chain.Key;
    public IReadOnlyList<ChainDefinition> All { get; } = [chain];
    public bool TryGet(string key, out ChainDefinition definition)
    {
        definition = chain;
        return key == chain.Key;
    }
    public ChainDefinition GetRequired(string key) => chain;
}
