# Plan: reach strict Clean Architecture

Written 2026-08-06. This is the follow-on to
[`clean-architecture-plan.md`](clean-architecture-plan.md), whose scoped ratchet is green.

**Status: complete, with Phase 4 explicitly skipped at the user's direction.** All hosted adapters
and external providers live in Infrastructure, Worker contains `Program.cs` only, Agentic Commerce
event messages and reconciliation policy live in Application behind its staged-write port, Web uses
the Application-facing Identity account boundary, and the three proven aggregates now own their
pricing, eligibility, lifecycle, and idempotency invariants.

## Outcome

The completed codebase has this dependency graph:

```
Domain          -> (nothing)
Application     -> Domain
Infrastructure  -> Application, Domain
Web             -> Application, Infrastructure (composition root only)
Worker          -> Application, Infrastructure (composition root only)
```

`Web` and `Worker` remain delivery mechanisms. Other than `Program.cs`, design-time EF tooling,
and framework configuration, neither host may reference EF, `ApplicationUser`, or concrete
Infrastructure services. `Infrastructure` owns database, ASP.NET Identity, RPC, blockchain, and
background-process adapters; `Application` owns all use-case orchestration; `Domain` owns the
invariants that make its entities valid.

This is intentionally stricter than the existing scoped plan. The current reconciliation-adapter
exceptions are decisions, not defects; this plan explicitly overrides that decision because the
goal is now canonical—not merely pragmatic—Clean Architecture.

## Completion criteria

1. Architecture tests have no temporary migration allowlists; `CompositionRoots` contains only
   the two composition roots and the Web design-time DbContext factory.
2. No non-composition type in Web or Worker references `AppDbContext`, EF, `ApplicationUser`, or
   a concrete Infrastructure type.
3. The Worker project contains `Program.cs` and host-only bootstrap code; reconciliation and
   external-service adapters live in Infrastructure.
4. Domain entities expose behaviour for their existing invariants, with private setters for
   invariant-bearing state. EF mappings and migrations are reviewed for every affected aggregate.
5. Application presents individually testable policies behind its services. The optional split
   into one interface per use case is deliberately excluded from this migration.
6. Build, architecture, unit, and the real PostgreSQL integration suite are green. The fixture is
   now available through `scripts/apple-container-postgres.sh start` and the 17 integration tests
   pass when `TEST_POSTGRES_CONNECTION` is set.

## Phase 0 — strengthen the ratchet first

Do this before moving source files.

- Split the architecture test exemptions into `CompositionRoots` and focused temporary migration
  allowlists. The latter start with the six hosted adapters and their three external provider
  files, then count down to zero.
- Add a source rule for non-composition Web references to
  `ThisCafeteria.Infrastructure.Identity.ApplicationUser` and `UserManager<ApplicationUser>`.
- Add a source rule that Worker contains no hosted-service implementation or Nethereum/EF usage
  after the move; Program is the sole Worker allowlist entry.
- Add a reflection rule ensuring every Application use-case interface is implemented only by
  Application or Infrastructure—not a host.
- Keep equality-based allowlists. Each mechanical move deletes its matching entry in the same
  change.

Exit gate: the suite is green before production code moves, with a precise temporary list.

## Phase 1 — turn reconciliation into Infrastructure adapters

Move these hosted adapters from Worker to Infrastructure:

- `AgenticCommerceReconciliationWorker`
- `ChainReconciliationSupervisor`
- `SolanaReconciliationSupervisor`
- `StakingLedgerReconciliationWorker`
- `CrossChainSolverWorker`
- `OrderProcessingWorker`

Move their Nethereum/RPC implementations as well (`EvmEscrowEventProvider`,
`CrossChainIntentProvider`, `EvmCrossChainSolverExecutor`) so Infrastructure never depends on
Worker. Promote only the contracts and value messages that cross a use-case boundary—chain
definitions, decoded escrow/registry events, and solver intents—to Application. Keep provider
implementations internal to Infrastructure wherever no other layer needs their interface.

Move `AgenticCommerceReconciliationApplicator` into Application alongside
`IAgenticCommerceProjectionBatch`: it is policy over a narrow port and currently has no EF
dependency. The Infra hosted adapter invokes it; its EF batch stays Infra-owned.

Add `AddReconciliationInfrastructure(IConfiguration)` to register all moved hosted services,
providers, and application services. `ThisCafeteria.Worker/Program.cs` becomes configuration plus
calls to Infrastructure registration methods.

**Non-negotiable transaction rule:** each reconciliation pass must retain one scoped
`AppDbContext`, one transaction, staged projection/checkpoint writes, one `SaveChangesAsync`, and
one commit. Do not replace this with per-event repositories that save independently.

Exit gate: no reconciliation implementation remains in Worker; worker-focused unit tests and a
new integration test prove a failed write leaves every affected checkpoint unchanged.

## Phase 2 — complete the Identity boundary

Replace all Web usages of `UserManager<ApplicationUser>` in the identified controllers and Blazor
components with Application-facing contracts.

- Define `IIdentityAccountService` in Application for account lookup/creation, wallet binding,
  admin password-sign-in operations, and user-profile identity resolution. Its API accepts and
  returns IDs and purpose-built DTOs, never `ApplicationUser`, `IdentityResult`, or EF exceptions.
- Implement it in Infrastructure over `UserManager<ApplicationUser>`, `SignInManager`, and the
  existing wallet identity repositories. Translate framework failures to Application results.
- Keep HTTP and circuit concerns in Web: controllers/components obtain the authenticated claim
  subject through `HttpContext.User` or `AuthenticationStateProvider`, then pass the subject ID to
  Application. They do not query Identity stores directly.
- Move the remaining wallet-auth orchestration out of `WalletAuthController` into an Application
  use case backed by the new port. Preserve the existing different duplicate-response semantics
  for staking endpoints.
- Restrict direct Identity registration and cookie configuration in `Web/Program.cs` to the
  composition root.

Exit gate: the Identity source rule is empty, wallet-auth/staking/profile integration tests are
green, and a controller test verifies framework errors do not leak over HTTP.

## Phase 3 — enrich only the three proven aggregates (complete)

Do not convert all thirty entities. Start with the duplicated invariants already identified.

1. **Order.** Introduce an order factory/reconstitution path and behaviour for pricing,
   coupon application, item addition, payment recording, and allowed status transitions.
   Move arithmetic from `OrderPricingService` into a domain value object or the aggregate; leave
   only request-to-domain mapping and repository orchestration in Application.
2. **Coupon.** Add `CanBeRedeemedBy`/`ValidateFor` behaviour for activation, minimum order,
   validity window (if introduced), and redemption policy inputs. Keep the repository lookup for
   historic redemptions in Application, then call the aggregate for its own rules.
3. **StakingLedgerEntry.** Add a value object or factory for the immutable on-chain identity
   `(chain key, transaction hash, operation index)` and normalise it in one place. Repositories
   use it for lookup/uniqueness; callers stop reconstructing the key themselves.

For every aggregate: add a private parameterless constructor for EF, make invariant-bearing
setters private, and retain a reconstitution path for existing rows. These changes preserve the
existing schema and mappings, so no migration was generated; PostgreSQL integration verification
confirmed EF materializes the private-set entities correctly.

Exit gate per aggregate: domain-only unit tests cover its invariant without EF; mapping tests and
the PostgreSQL integration suite pass; migration SQL has been reviewed.

## Phase 4 — expose application use cases explicitly (skipped by request)

Retain the current services as compatibility façades. This skipped optional refinement would split public policies
into focused contracts such as `CreateOrder`, `GetOrdersForUser`, `QuoteCoupon`, `RedeemCoupon`,
`RecordStakingLedgerEntry`, and wallet-auth flows. A use case gets one request, one response, and
the ports it needs; it does not depend on MVC, Blazor, Identity, EF, or Nethereum types.

Migrate one endpoint at a time. Delete a façade method only after every caller has moved. Do not
add MediatR unless the migration explicitly adopts request dispatch/CQRS across the application;
interfaces and DI registrations are sufficient here.

Exit gate: every controller action delegates to one named use case or an intentionally thin query
facade, and each use case has isolated unit coverage.

## Phase 5 — remove the final exceptions and prove it (complete)

- Delete the six hosted-adapter entries and three provider entries from the migration allowlists as
  their moves land.
- Keep `CompositionRoots` limited to the three necessary concrete locations and document why each
  remains an exception.
- Run `dotnet build ThisCafeteria.sln`, the architecture suite, all unit tests, and all 17
  PostgreSQL integration tests using the Apple Container fixture.
- Boot Web and Worker with the real fixture connection to verify DI construction; exercise a
  reconciliation pass and wallet-auth flow.
- Update `README.md` and the handoff prompt with the new strict boundaries and remove obsolete
  pragmatic-exemption language.

## Delivery order and review discipline

Land Phases 0–2 as small, independently deployable changes. Treat each domain aggregate in Phase
3 as a separate pull request and Phase 4 as endpoint-sized pull requests. No phase may bundle a
schema change, a host relocation, and an identity rewrite: that would make a behavioural regression
too difficult to isolate.
