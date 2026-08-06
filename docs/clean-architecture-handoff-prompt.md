# Handoff prompt: finish the Clean Architecture migration in ArtisanalBrew

Copy everything below the line into a fresh agent session.

---

You are continuing a Clean Architecture migration in the ArtisanalBrew repository
(`ThisCafeteria.sln`, .NET 10, Blazor Server + background Worker). Substantial work is already
done. **Read `docs/clean-architecture-plan.md` first** — it records what was done, what was
deliberately not done, and why. Do not redo decisions recorded there without saying why you're
overriding them.

For the stricter, canonical follow-on migration, read
`docs/full-clean-architecture-plan.md` after the historical plan.

## Current state

The dependency graph is correct and compiler-enforced: Domain → nothing, Application → Domain,
Infrastructure → Application + Domain, Web/Worker → Application + Infrastructure.

`tests/ThisCafeteria.ArchitectureTests` enforces the scoped invariants and strict-migration
ratchets in `KnownViolations.cs`. The permanent sets and all Worker migration sets are empty:
Worker contains only `Program.cs`; its hosted, RPC, Service Bus, and EF adapters now live in
Infrastructure, while Agentic Commerce reconciliation policy and event messages live in Application.
The Web Identity set is empty: controllers and components consume `IIdentityAccountService` and
claims, while the Infrastructure adapter owns ASP.NET Identity. `ThisCafeteria.Web`
no longer touches Entity Framework at all. There are 12 repository interfaces plus `IUnitOfWork`
and the staged-write `IAgenticCommerceProjectionBatch` in `Application/Repositories/`.

## Your goal

Close the remaining gaps between this codebase and Clean Architecture. They are, in the order I'd
recommend tackling them:

### 1. The Identity boundary (complete)

`IIdentityAccountService` in Application is implemented by Infrastructure over ASP.NET Identity.
Web reads the authenticated claim subject itself and calls that contract for account lookup, wallet
binding, sign-in/out, and deletion. The explicit Web Identity allowlist is empty. Keep framework
types, `ApplicationUser`, `IdentityResult`, and EF exceptions out of Application and Web.

### 2. The domain model (complete)

Only the three proven aggregates were enriched. `Order` owns pricing, placement, payment recording,
and lifecycle transitions; `Coupon` owns terms and eligibility; `StakingLedgerEntry` owns its
validated chain/transaction/operation identity. Their invariant-bearing state has private setters
and existing EF mapping was verified against PostgreSQL without a schema migration.

### 3. Use-case granularity (optional, explicitly skipped)

Application intentionally retains coarse services (`IOrderService` with several methods), which is
the common .NET reading of the pattern. The user explicitly excluded splitting them into one
interactor per use case. **A MediatR reference was deliberately deleted from this codebase because
it declared a pattern the code did not have — do not reintroduce it unless adopting CQRS end to end.**

## Traps that will bite you

These are all learned the hard way in this repo. Violating any of them is a correctness regression,
not a style disagreement.

1. **The Worker's unit of work is the whole reconciliation pass, not the individual write.** Each
   loop opens a transaction, stages many events into the change tracker *without saving*, advances
   a checkpoint, then does one `SaveChangesAsync` and commits. `ApplyEventAsync` deliberately never
   saves. Every repository in this solution saves per call — applying that pattern in the Worker
   would break checkpoint atomicity. If you need staged writes, make them `void` (not `Task`) so
   the type states that no I/O happens.

2. **`StakingLedgerRepository` is backed by `IDbContextFactory`, not the scoped `AppDbContext`.**
   It is read from Blazor components, whose scope is the whole circuit, so concurrent renders would
   otherwise share a non-thread-safe DbContext. Consequence: it is **not** covered by `IUnitOfWork`,
   which wraps the scoped context. Never mix it into a transaction expecting coverage.

3. **EF exception types must not cross into Application.** `DbUpdateException` is how the database
   reports a duplicate. Repositories surface that as a return value instead — see
   `IStakingLedgerRepository.AddIfAbsentAsync` returning `StakingLedgerWriteResult`, and
   `IRewardClaimRepository.TryAddAsync` returning `bool`. Follow that pattern.

4. **`StakingController` and `LiquidStakingController` treat duplicate submissions differently on
   purpose** — Conflict vs. OK-with-existing-row. That is why they have two services rather than
   one. Do not unify them.

5. **Search `ThisCafeteria.Application` for an existing helper before promoting one out of a host.**
   A base58 codec existed in four separate copies in this repo, and one round of this migration
   added a fifth by not checking first. The architecture tests catch misplacement, not duplication.

6. **The architecture tests assert set equality, not "no new violations".** Fixing a file means
   deleting its allowlist line in the same commit, or the suite fails on the stale entry. That is
   intentional.

## Verification — required before you report anything done

```bash
dotnet build ThisCafeteria.sln                                              # must be 0 errors
dotnet test tests/ThisCafeteria.ArchitectureTests/ThisCafeteria.ArchitectureTests.csproj
dotnet test tests/ThisCafeteria.UnitTests/ThisCafeteria.UnitTests.csproj    # 356 passing at handoff
dotnet test tests/ThisCafeteria.IntegrationTests/ThisCafeteria.IntegrationTests.csproj
```

**Integration tests fail 10/17 without a database.** They require `TEST_POSTGRES_CONNECTION` from
an Apple Container PostgreSQL fixture. That failure count is the *baseline*, identical before and
after all work so far — confirm it hasn't grown rather than assuming the suite is broken.

**This is the weakest part of the verification so far, and you should fix it if you can.** The
staking, rewards, and wallet-auth paths were refactored with no passing integration coverage. If
you can get the fixture running, do that first and establish a real green baseline before changing
more code.

DI wiring is not covered by any test. To check it, boot the app with a connection string set so the
full graph is constructed:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=nonexistent;Username=x;Password=y" \
dotnet run --project src/ThisCafeteria.Web/ThisCafeteria.Web.csproj
```

Startup should log "Now listening on" with no `Unable to resolve service` lines. Note that
`Program.cs` gates many registrations on `hasDatabase` — preserve that; it exists so
Development-mode `ValidateOnBuild` doesn't trip on a dangling `AppDbContext` dependency.

## Working agreement

- Keep `docs/clean-architecture-plan.md` and `docs/full-clean-architecture-plan.md` current as
  you go. They are the project's memory.
- Preserve behavior. This is a refactor. Where you must choose between a tidier shape and identical
  behavior, choose identical behavior and say so.
- If you conclude a remaining item is not worth doing, say that plainly in the strict plan rather
  than silently weakening a ratchet. `KnownViolations.CompositionRoots` is reserved for the three
  unavoidable concrete bootstrap locations.
- The working tree may contain unrelated in-progress edits (`ProcurementLab.razor`,
  `YieldPanel.razor`). Check `git status` before you start and don't sweep them into your commits.
