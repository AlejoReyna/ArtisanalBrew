# Plan: Bring ArtisanalBrew to full Clean Architecture

Written 2026-08-06 against `agent/profile-editor-space-assets`.

**Status: complete, as scoped.** Closed out 2026-08-06.

Phases 0, 1, 3 and 6 are done. Phase 2 is done for `ThisCafeteria.Web`; five Worker files were
reviewed and **deliberately exempted** rather than changed (reasoning below). The remaining
applicator defect was closed with an Application staged-write port. Phases 4 and 5 were assessed
and **not done**, on merit.

All five definition-of-done invariants now hold with no debt exceptions.

| Ratchet set | State |
|---|---|
| `MisplacedApplicationImplementations` | empty |
| `DataAccessInRazorComponents` | empty |
| `UnusedArchitecturalPackages` | empty |
| `DataAccessOutsideInfrastructure` | empty |

### Applicator port (done 2026-08-06)

`Worker/AgenticCommerceReconciliationApplicator.cs` no longer takes `AppDbContext` on its public
interface. It instead depends on `IAgenticCommerceProjectionBatch`, declared in
`Application/Repositories/` and implemented in Infrastructure over the scoped `AppDbContext`.

The port exposes async reads and `void` staging methods. `void` is intentional: it makes the
no-save invariant part of the API. The reconciliation worker resolves the batch from the same
scope as its `AppDbContext`, stages all projections and advances the checkpoint, then remains the
sole owner of `SaveChangesAsync` and transaction commit. Atomicity is therefore unchanged.

`AgenticCommerceReconciliationApplicatorPortTests` now exercises an event lifecycle with an
in-memory fake batch and no SQLite connection. The existing SQLite tests remain to cover EF
mapping and transactional integration. The allowlist entry was deleted in the same change.

### Why the other five Worker files were exempted rather than fixed

Each polls an RPC endpoint, opens a transaction, writes, and advances a checkpoint - an adapter
between two external systems, not application policy. The Worker is a host, and a host's hosted
services are adapters, so leaving them there is a defensible reading rather than a compromise.

Routing them through repositories was considered and rejected on correctness grounds. Their unit
of work is the whole reconciliation pass: events are staged into the change tracker and one
`SaveChangesAsync` commits them together with the checkpoint. Every repository in this solution
saves per call, which would break that atomicity - and a repository per checkpoint table would be
abstraction invented to satisfy the architecture test rather than to serve the design.

They now live in `KnownViolations.DeliberateExemptions` alongside the composition roots, not in a
list of debt.

### Why Phases 4 and 5 were not done

Both were judged poor value against their cost, and the plan flagged them as optional from the
start.

- **Phase 4 (Identity boundary).** Introducing `ICurrentUserService` would touch eight files to
  abstract away a framework type that ASP.NET applications idiomatically use directly. One real
  instance did get fixed in passing: the EF query behind `userManager.Users` moved into
  `Infrastructure/Identity/UserManagerWalletExtensions.cs`, where the persistence dependency
  belongs.
- **Phase 5 (domain enrichment).** Converting 30 anemic entities changes EF mappings and needs
  migration review, for a codebase whose invariants are already centralised in Application
  services and now covered by them.

Reopen either if the pain becomes concrete. Neither is blocking anything today.

## Where we are

The project-reference graph is already correct and compiler-enforced:

```
Domain  ->  (nothing)
Application  ->  Domain
Infrastructure  ->  Application, Domain
Web / Worker  ->  Application, Infrastructure
```

Domain has zero `PackageReference` entries. Application and Domain have zero
`Microsoft.EntityFrameworkCore` references. All seven repository interfaces in
`Application/Repositories/` have matching implementations in
`Infrastructure/Persistence/Repositories/`. The commerce slice (products, orders,
coupons, profiles, transparency) is a textbook implementation —
`Web/Controllers/OrdersController.cs` is 47 lines that inject only `IOrderService`
and `IProfileService`.

The drift is confined to the blockchain / wallet / staking slice, which was built
later and skipped the Application layer. Everything below targets that slice.

## Definition of done

Five invariants, each expressible as an automated test:

1. No type outside `ThisCafeteria.Infrastructure` references `AppDbContext` or any
   `Microsoft.EntityFrameworkCore` type, apart from the composition roots and the
   reconciliation adapters recorded in `KnownViolations.DeliberateExemptions`.
2. `ThisCafeteria.Web` contains no implementation of an interface declared in
   `ThisCafeteria.Application`. Web holds controllers, components, and HTTP-bound
   adapters only.
3. Every `Application` interface that has a runtime implementation has it in
   `Infrastructure`.
4. No `.razor` component performs data access.
5. No unused architectural dependencies.

## Phase 0 — Build the ratchet first

Add `tests/ThisCafeteria.ArchitectureTests`, encoding the five invariants above. Seed
each rule with an explicit allowlist of the violations that exist today, so the suite
is green on day one and any *new* violation fails immediately. Each later phase
deletes entries from the allowlist.

**Built without NetArchTest**, which the first draft of this plan assumed. Two reasons:
source scanning reports a real `path:line` a developer can jump to, and it sees
`.razor` files, which compile to generated types whose names no longer resemble the
component that produced them. NetArchTest.Rules is also a 2022-era package over
Mono.Cecil, which is a risk to take on .NET 10 for no gain here. Interface placement
*is* checked by reflection, since "implements interface X" is a type-system fact that
only loaded metadata knows reliably — and that rule immediately found three misplaced
gateways the file survey had missed.

The rules assert **set equality**, not "no new violations", so a stale allowlist entry
fails the build exactly as loudly as a regression. That is what stops the list rotting.

This goes first for a reason. The graph was correct in the commerce slice and drifted
in the blockchain slice precisely because nothing enforced it. Without the ratchet,
the same drift resumes the week after this work lands.

Cost: small. Risk: none. Fully parallelizable with everything else.

## Phase 1 — Relocate misplaced Infrastructure (mechanical)

Roughly 2,000 lines of chain-integration code implement Application interfaces but
live in `Web/Services/Blockchain/`. Nothing about them is presentation code.

**Verified precondition:** none of these files reference `IHttpContextAccessor`,
`IJSRuntime`, `NavigationManager`, `ProtectedBrowserStorage`, `ComponentBase`, or
`ISession`. Only `SelectedChainAccessor`, `SelectedSmartAccountAccessor`,
`CartMutationClient`, and `ShoppingCartService` do — and those correctly stay in Web,
since cookie/session/circuit handling is genuinely a presentation concern.

Move to `Infrastructure/Services/Blockchain/`:

| File | Lines | Interface today |
|---|---|---|
| `CoffeeWeb3Service.cs` | 678 | `Application.Services.Blockchain.ICoffeeWeb3Service` |
| `SolanaLiquidStakingGateway.cs` | 320 | Application |
| `SolanaTransactionBuilder.cs` | 242 | none — define one |
| `EvmLiquidStakingGateway.cs` | 161 | Application |
| `EvmMarketplacePaymentGateway.cs` | 91 | `IMarketplacePaymentGateway` |
| `SolanaFaucetSecret.cs` | 84 | none — internal helper |
| `CoinGeckoEthUsdPriceService.cs` | 77 | none — define one |
| `ContractAbis.cs` | 55 | none — internal helper |

For the three with no Application interface, declare one in
`Application/Services/Blockchain/` as part of the move.

Update the `AddInfrastructure` registrations and delete the corresponding lines from
`Program.cs`. **Preserve the `hasDatabase` conditional** at `Program.cs:163-180` — it
exists so ASP.NET's Development-mode `ValidateOnBuild` doesn't trip on a dangling
`AppDbContext` dependency, and that comment at `Program.cs:159-161` is load-bearing.

**Payoff beyond tidiness:** `ThisCafeteria.Worker` references Application +
Infrastructure but not Web, so today it cannot reuse any of this. It has eleven
background services — including `StakingLedgerReconciliationWorker` and
`SolanaReconciliationSupervisor` — that reconcile exactly the chains these gateways
talk to. This move is what makes that code reachable.

Cost: moderate but low-risk. No logic changes; verify by compiling and running the
existing suites.

### Phase 1 outcome (done)

All nine gateway files plus `RewardClaimService` moved. Three supporting relocations were needed
that the survey had not predicted:

- `IEthUsdPriceService` was declared in Web, not Application. Moved to
  `Application/Services/Blockchain/`.
- `CoffeeCoinOwnerOptions` was the sole file in `Web/Configuration/`. Moved to
  `Application/Configuration/` beside `BlockchainNetworkOptions`; that directory is now gone.
- **`SolanaBase58` existed as two byte-identical `internal` copies** - one in the Solana staking
  gateway, one in `Worker/SolanaReconciliationSupervisor.cs`. Neither could see the other across
  the assembly boundary, so the codec had simply been pasted twice. Promoted to a single public
  `Application/Services/Blockchain/SolanaBase58.cs` and both copies deleted. The body was moved
  **verbatim**: a base58 codec is where a well-meaning reformat silently corrupts an address.

That duplication is the concrete argument for this whole exercise. The layering was not merely
untidy - it had already caused a correctness-sensitive function to be maintained in two places,
where a fix to one copy would never have reached the other.

Infrastructure gained two packages it now needs: `BouncyCastle.Cryptography` (Ed25519 signing)
and `Microsoft.Extensions.Http` (`IHttpClientFactory`). Moved files also needed an explicit
`using Microsoft.Extensions.Logging;` - Infrastructure is a classlib and does not inherit the Web
SDK's implicit usings.

Registrations moved out of `Program.cs` into a new
`DependencyInjection.AddBlockchainInfrastructure(IConfiguration)`. `IRewardClaimService` went into
the existing database-gated block, since it depends on `IRewardClaimRepository`. What stayed in
the host is the presentation-bound state - `WalletDashboardState`, `ProfileAvatarState`, and the
cookie-backed accessors - which genuinely depend on `HttpContext`.

Verified: solution builds with 0 errors; 352 unit tests and 7 architecture tests pass. Integration
tests fail 10/17 both before and after the change - they require a PostgreSQL fixture via
`TEST_POSTGRES_CONNECTION` that is not running locally, and the failure counts are identical at
`HEAD`. The app was booted with `hasDatabase` both false and true; the full DI graph builds with
zero resolution errors, and `/staking/api/coffee-balance`, which routes through the relocated
`ICoffeeWeb3Service`, returns 200.

## Phase 2 — Give the blockchain slice an Application layer (the real work)

This is the bulk of the effort. Six files in Web touch `AppDbContext` directly,
across 28 EF call sites and 8 `SaveChangesAsync` calls.

### 2a. New repository interfaces in `Application/Repositories/`

| Interface | Entity | Current direct-EF call sites |
|---|---|---|
| `IStakingLedgerRepository` | `StakingLedgerEntry` | `StakingController.cs:167,327,427`; `LiquidStakingController.cs:56,90,94`; `YieldPanel.razor:1102` |
| `IWalletIdentityRepository` | `WalletIdentity` | `WalletAuthController.cs:75,403,406,424` |
| `IWalletAuthChallengeRepository` | `WalletAuthChallenge` | `SolanaWalletChallengeService.cs:47,52,62,69,83` |
| `ISolanaFaucetClaimRepository` | `SolanaFaucetClaim` | `SolanaFaucetService.cs:94,103,122` |
| *(exists)* `IRewardClaimRepository` | `RewardClaim` | `RewardsController.cs:139,165,204` — repo exists, controller ignores it |

Implementations go in `Infrastructure/Persistence/Repositories/` alongside the
existing seven.

### 2b. Resolve the transaction problem — decide this before writing code

Two call sites open explicit EF transactions spanning multiple writes:

- `RewardsController.cs:115` — `dbContext.Database.BeginTransactionAsync`, then
  insert a claim at `:139` and a second `SaveChangesAsync` at `:165`.
- `WalletAuthController.cs:63` — a transaction wrapping wallet-identity lookup and
  ASP.NET Identity user creation.

A per-entity repository cannot express "these writes commit together." Options:

- **Recommended: add `IUnitOfWork` to Application** (`BeginTransactionAsync` returning
  a disposable handle with `CommitAsync`), implemented in Infrastructure over
  `AppDbContext`. Keeps the boundary honest and lets Application services orchestrate
  multi-entity operations.
- Alternative: push each transactional operation whole into a single
  Infrastructure-level service behind one Application interface. Less machinery, but
  it buries orchestration logic in Infrastructure where it's harder to unit-test.

The `WalletAuthController` transaction is the harder of the two because it spans
ASP.NET Identity's `UserManager`, which has its own persistence path. Expect this one
site to need care; it may be the one place where a coarse Infrastructure-level service
is the pragmatic answer.

### 2c. New Application services

- `IStakingLedgerService` — owns the record-if-not-already-recorded idempotency that
  `StakingController.cs:427` + `:167` currently open-codes, plus the ledger read that
  `YieldPanel.razor` performs.
- `ILiquidStakingLedgerService` — owns `LiquidStakingController`'s
  read-existing / insert / concurrency-retry loop (`:56,90,94`).
- `IRewardClaimService` — already declared at
  `Application/Services/Rewards/IRewardClaimService.cs:5`; move
  `RewardsController`'s transactional body into its implementation and relocate that
  implementation to Infrastructure.
- `IWalletAuthenticationService` — owns `WalletAuthController`'s authentication
  transaction.
- Promote `ISolanaFaucetService` (`Web/Services/Blockchain/SolanaFaucetService.cs:16`),
  `ISolanaWalletChallengeService` (`Web/Services/Wallet/SolanaWalletChallengeService.cs:16`),
  and `IWalletChallengeService` (`Web/Services/Wallet/WalletChallengeService.cs:19`)
  into `Application/Services/`, with implementations in Infrastructure.

Expected result: `StakingController` drops from 456 lines toward ~150, and
`WalletAuthController` from 564 toward ~200 — both becoming what
`OrdersController` already is.

Cost: this is the large phase. Sequence it per-controller so each step is
independently shippable and reviewable: LiquidStaking (smallest) → Staking → Rewards
→ WalletAuth (hardest, do last).

## Phase 3 — Get data access out of the presentation layer

`Components/Shared/YieldPanel.razor:23` injects `IDbContextFactory<AppDbContext>` and
runs a projection query at `:1102`. Replace with an injected `IStakingLedgerService`
returning the `StakingLedgerItem` shape it already builds.

**Coordinate on timing:** `YieldPanel.razor` and `YieldPanel.razor.css` are currently
modified in the working tree. Land or discard that work before touching this file.

Also worth noting while here: the comment at `Services/ProfileAvatarState.cs:19-22`
explains that YieldPanel takes an `IDbContextFactory` specifically to avoid contending
on the scoped `AppDbContext` under Blazor Server's concurrency model. The replacement
service must preserve that property — the repository implementation behind
`IStakingLedgerService` should use the factory internally, not the scoped context.
This is a real constraint, not incidental.

## Phase 4 — The Identity boundary

Eight files in Web import `ThisCafeteria.Infrastructure.Identity`, and eight use
`UserManager<ApplicationUser>`. There is no `ICurrentUserService` abstraction anywhere
in the solution.

Strict Clean Architecture says Web should not know Infrastructure's user type.
Recommended middle path: introduce `ICurrentUserService` in Application (exposing user
id, wallet address, admin flag) and use it from **controllers and Application
services**; leave Blazor components on `AuthenticationStateProvider` /
`UserManager`, which is idiomatic ASP.NET and where the abstraction buys least.

Flag this as a judgment call rather than a mandate — pursuing full purity here has a
poor effort-to-benefit ratio compared to Phases 1–3.

## Phase 5 — Domain enrichment (optional, do not big-bang)

Across 30 entities there are zero behavior methods and no encapsulated setters —
`Domain/Entities/Order.cs:5-34` is 25 public get/set properties. Domain currently
functions as a schema definition; all invariants live in Application services.

Do **not** convert all 30 entities. Enrich only where an invariant is already
duplicated across call sites:

- `Order` — total/discount arithmetic, currently in `OrderPricingService`.
- `StakingLedgerEntry` — the dedupe key that `StakingController.cs:427` and
  `LiquidStakingController.cs:56` each reconstruct by hand.
- `Coupon` — validity-window and redemption-limit checks.

Everything else can stay anemic. Note this changes EF mappings, so it needs migration
review; that is why it is last and optional.

## Phase 6 — Cleanup and close the ratchet

- Delete `<PackageReference Include="MediatR" Version="14.*" />` from
  `Application.csproj`. Zero `IRequestHandler` implementations and zero imports exist
  anywhere in `src/`. (If the intent was to adopt CQRS later, decide that explicitly —
  but an unused dependency shouldn't sit in the graph implying a pattern that isn't
  there.)
- Empty every Phase 0 allowlist and assert the rules unconditionally.
- Update `README.md` / `CLAUDE.md` with the enforced boundary rules so the next agent
  session inherits them.

## Explicitly out of scope

- `Application/Services/Blockchain/WalletAddressRules.cs:1` and
  `StakingCalldataDecoder.cs:3` import `Nethereum.Util` for checksum and ABI decoding.
  Purist Clean Architecture would hide this behind an abstraction. **Recommendation:
  keep it and document the exemption** — these are pure value-conversion functions with
  no I/O, and wrapping them adds indirection with no testability gain.
- `ThisCafeteria.AgentGateway` (TypeScript) — separate stack, separate concern.
- Cart and chain-selection services in Web — correctly placed, HTTP-bound by nature.

## Sequencing

Phase 0 is independent and should land first. Phase 1 is mechanical and can proceed in
parallel with Phase 0. Phase 2 depends on Phase 1 (services must be in Infrastructure
before controllers can be rewired through Application). Phase 3 depends on 2a/2c.
Phases 4 and 5 are independent judgment calls and can be deferred indefinitely without
blocking the definition of done — items 1–4 of it are satisfied by Phases 0–3 and 6.

Rough shape of the effort: Phase 2 is roughly two-thirds of the total; Phases 0, 1, 3,
and 6 together are the other third.

## Verification per phase

`tests/ThisCafeteria.UnitTests` (42 files) and `tests/ThisCafeteria.IntegrationTests`
(10 files) both already reference Web, Infrastructure, Application, Domain, and Worker,
so they will compile against the moved types without csproj changes. Run both suites
plus a `dotnet build` of the full solution after every phase. Integration coverage of
the staking/wallet paths is thin (10 files total), so Phase 2 should add service-level
tests as it extracts each service — that extraction is the first time this logic
becomes unit-testable without an HTTP host.
