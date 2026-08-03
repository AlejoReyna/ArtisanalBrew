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
// In "negative" mode (contracts/evm/scripts/crossstack-bundler-submit-negative-check.ts), the
// REAL SponsorshipPolicyService is configured to genuinely deny the operation for one specific
// reason named by CROSSSTACK_NEGATIVE_CASE, the REAL UserOperationSponsor is asked to sponsor it,
// and whatever unapproved SponsorshipSignature that actually produces is handed to the REAL
// UserOperationSubmitter. This is the difference from the fabricated SponsorshipSignature.Deny(...)
// the "submit" mode's `denied` flag uses: here the denial is produced by the policy engine under
// test rather than asserted by the test, so it proves the policy reason AND the submitter's refusal
// in one path, which is what the plan's Phase 4 gate actually asks for.
//
// This is not wired into `dotnet test` because it requires a live Hardhat node with the canonical
// EntryPoint/factory/paymaster already deployed (and, for "submit", a running Rundler bundler).
// Run it via the Hardhat scripts:
//   HARDHAT_NETWORK=localhost npx tsx scripts/crossstack-sponsor-check.ts
//   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-bundler-submit-check.ts
//   HARDHAT_NETWORK=arbitrumLocal npx tsx scripts/crossstack-bundler-submit-negative-check.ts

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ThisCafeteria.CrossStackHarness <path-to-op-description.json> [approve|wrongtarget|submit|negative]");
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

// "negative" mode's specific denial to provoke. Each case below rigs exactly ONE input so the
// resulting SponsorshipDenialReason is unambiguous - if a case ever started denying for a
// different reason than the one it rigs, the calling script's reason assertion catches it rather
// than accepting "some denial happened" as proof.
var negativeCase = (Environment.GetEnvironmentVariable("CROSSSTACK_NEGATIVE_CASE") ?? string.Empty).ToLowerInvariant();

// "wrongtarget" mode proves a policy denial yields no signature at all, by pointing the
// allowlist at an address the operation does NOT call.
var allowedTarget = mode == "wrongtarget" || negativeCase == "wrongtarget"
    ? "0x000000000000000000000000000000000000dead"
    : target;

// The selector equivalent: an allowlisted selector the operation's callData does NOT begin with.
var allowedSelector = negativeCase == "wrongselector" ? "0xdeadbeef" : selector;

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
    AllowedSelectors = [allowedSelector],
    VerifyingSignerPrivateKey = DevSignerKey,
    NativeCurrencyUsdRate = 3000m,
    // Mined-but-reverted sponsored operations tolerated before the grant is auto-revoked. Kept
    // overridable so crossstack-sponsored-delegation-check.ts can reach the threshold in a couple
    // of operations instead of paying for the production default's worth of reverts.
    MaxRevertedOperations = int.TryParse(Environment.GetEnvironmentVariable("CROSSSTACK_MAX_REVERTED_OPERATIONS"), out var parsedMaxReverted)
        ? parsedMaxReverted
        : new SponsorshipPolicyOptions().MaxRevertedOperations
};

// In-memory by default: every invocation is independent, which is what the single-shot proofs want.
// CROSSSTACK_DB_PATH switches to a file so grant state SURVIVES across invocations - required by
// crossstack-sponsored-delegation-check.ts's revocation case, which has to submit several
// operations through separate harness processes and watch one grant accumulate reverts.
var dbPath = Environment.GetEnvironmentVariable("CROSSSTACK_DB_PATH");
using var connection = new SqliteConnection(
    string.IsNullOrWhiteSpace(dbPath) ? "DataSource=:memory:" : $"DataSource={dbPath}");
connection.Open();
using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
db.Database.EnsureCreated();
// A budget far above any plausible local gas cost, and a validity window comfortably around now,
// so that in every mode except the "negative" cases that deliberately rig one of them, the grant
// itself is never the reason anything fails.
var grantNow = DateTime.UtcNow;
// Seed once. With a persistent CROSSSTACK_DB_PATH a re-seed would wipe the accumulated
// RevertedOperationCount the revocation proof is trying to observe.
var grantOwner = Owner.ToLowerInvariant();
if (!db.SponsorshipGrants.Any(g => g.ChainKey == "evm-local" && g.OwnerAddress == grantOwner))
{
    db.SponsorshipGrants.Add(new SponsorshipGrant
    {
        ChainKey = "evm-local",
        OwnerAddress = grantOwner,
        // A cost measured by real simulation always exceeds a sub-cent budget, so "overbudget" denies
        // on the measured figure rather than on a number this harness chose.
        BudgetUsd = negativeCase == "overbudget" ? 0.000001m : 100m,
        SpentUsd = 0m,
        // Zero means "no per-operation cap" (see SponsorshipPolicyService.EvaluateGrant).
        MaxOperationCostUsd = negativeCase == "overcap" ? 0.000001m : 0m,
        ValidFromUtc = negativeCase == "expired" ? grantNow.AddDays(-2) : grantNow.AddDays(-1),
        ValidUntilUtc = negativeCase == "expired" ? grantNow.AddDays(-1) : grantNow.AddDays(1)
    });
    db.SaveChanges();
}

var registry = new SingleChainRegistry(chain);
var policy = new SponsorshipPolicyService(db, registry, options, TimeProvider.System, NullLogger<SponsorshipPolicyService>.Instance);

if (negativeCase == "revoked")
{
    // Revoke through the real service rather than setting RevokedAtUtc by hand, so the proof
    // covers the revocation path itself and not just a hand-written database column value.
    await policy.RevokeAsync("evm-local", Owner);
}

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
    // A remote bundler may take several seconds to serve a receipt while it waits on its upstream
    // RPC. The denied proof never invokes this client (it returns before submission), so a
    // realistic timeout here does not weaken its fast no-network assertion.
    using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    var bundlerClient = new RundlerBundlerClient(httpClient, registry);
    var confirmations = new EntryPointConfirmationReader(registry);
    var submitter = new UserOperationSubmitter(registry, bundlerClient, confirmations, policy, TimeProvider.System, NullLogger<UserOperationSubmitter>.Instance);

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

    // Grant state is reported alongside the submission so a caller using a persistent
    // CROSSSTACK_DB_PATH can observe reverts accumulating and the grant being auto-revoked, without
    // reading the harness's database itself.
    var finalGrant = db.SponsorshipGrants.FirstOrDefault(g => g.ChainKey == "evm-local" && g.OwnerAddress == grantOwner);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = submission.Status.ToString(),
        detail = submission.Detail,
        userOpHash = submission.UserOperationHash,
        transactionHash = submission.TransactionHash,
        costUsd = submission.CostUsd,
        grantRevertedCount = finalGrant?.RevertedOperationCount ?? 0,
        grantRevoked = finalGrant?.RevokedAtUtc is not null,
        grantSpentUsd = finalGrant?.SpentUsd ?? 0m
    }));

    // Consistent with "approve"/"wrongtarget" mode below: this process's job is to report the real
    // outcome, not to decide whether that outcome is the one the calling script wanted. A denial
    // is the CORRECT result for crossstack-bundler-submit-denied-check.ts and would be the wrong
    // one for crossstack-bundler-submit-check.ts - only the caller knows which. Always exit 0 here
    // and let the caller inspect `status` itself, the same as it already does for `approved`.
    return 0;
}

var result = await sponsor.SponsorAsync(operation);

if (mode == "negative")
{
    // The whole point: the SponsorshipSignature handed to the submitter below is the one the real
    // policy engine just produced for the rigged case - never a fabricated Deny(...). If the policy
    // wrongly APPROVED a case it should have denied, this still forwards that real approval to the
    // submitter, and the resulting submission attempt against an unreachable bundler fails loudly
    // instead of quietly reporting a denial the test wanted to see.
    using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    var bundlerClient = new RundlerBundlerClient(httpClient, registry);
    var confirmations = new EntryPointConfirmationReader(registry);
    var submitter = new UserOperationSubmitter(registry, bundlerClient, confirmations, policy, TimeProvider.System, NullLogger<UserOperationSubmitter>.Instance);

    // A well-formed owner signature, so that a `Denied` result can only come from the sponsorship
    // check and never from SubmitAsync's separate malformed-signature guard.
    var ownerSignature = $"0x{new string('a', 130)}";
    var submission = await submitter.SubmitAsync(operation, result, ownerSignature);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        negativeCase,
        approved = result.Approved,
        reason = result.Reason.ToString(),
        detail = result.Detail,
        submissionStatus = submission.Status.ToString(),
        submissionDetail = submission.Detail
    }));

    return 0;
}

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
