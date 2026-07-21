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
// pricing it, evaluating the sponsorship policy, and producing a paymaster signature. The
// TypeScript side then submits that signature to the canonical on-chain EntryPoint and checks
// whether it's accepted.
//
// The point of doing it this way, rather than testing everything in one language, is that a
// signature or a cost figure that only this codebase's own logic agrees with proves nothing — the
// real EntryPoint and paymaster contracts are the arbiter, and C# and Solidity have to agree with
// THEM, not with each other.
//
// This is not wired into `dotnet test` because it requires a live Hardhat node with the canonical
// EntryPoint/factory/paymaster already deployed. Run it via the Hardhat scripts:
//   HARDHAT_NETWORK=localhost npx tsx scripts/crossstack-sponsor-check.ts

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ThisCafeteria.CrossStackHarness <path-to-op-description.json> [approve|wrongtarget]");
    return 1;
}

var opPath = args[0];
var mode = args.Length > 1 ? args[1] : "approve";
var doc = JsonDocument.Parse(File.ReadAllText(opPath)).RootElement;

string ReadString(string key) => doc.GetProperty(key).GetString()!;
System.Numerics.BigInteger ReadBigInteger(string key) => System.Numerics.BigInteger.Parse(doc.GetProperty(key).GetString()!);

// Hardhat's first well-known development account and its published private key. Neither is a
// secret — they control nothing outside a local test node — and are used here only because this
// harness never touches a real chain.
const string Owner = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
const string DevSignerKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";

var target = ReadString("target");
var selector = ReadString("selector");

// "wrongtarget" mode proves a policy denial yields no signature at all, by pointing the
// allowlist at an address the operation does NOT call.
var allowedTarget = mode == "wrongtarget" ? "0x000000000000000000000000000000000000dead" : target;

var chain = new ChainDefinition
{
    Key = "evm-local",
    Family = ChainFamily.Evm,
    EvmChainId = 31337,
    EvmChainIdHex = "0x7a69",
    PublicRpcUrl = "http://127.0.0.1:8545",
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

var result = await sponsor.SponsorAsync(new SponsoredUserOperation
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
});

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
