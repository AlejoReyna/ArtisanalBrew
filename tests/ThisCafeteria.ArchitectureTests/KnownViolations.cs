namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// The ratchet.
///
/// The rules assert <b>set equality</b>, not "no new violations" - so a stale entry fails the
/// build just as loudly as a new violation does. Fixing a file means deleting its line here in
/// the same commit; the suite will not let these lists drift out of date.
///
/// Two kinds of entry, and the difference is the point:
///
/// <list type="bullet">
/// <item><b>Violation sets</b> are debt we intend to pay. The goal for each is empty.</item>
/// <item><see cref="TemporaryWorkerHostedServices"/>,
/// <see cref="TemporaryWorkerExternalAdapters"/>, and
/// <see cref="TemporaryWebIdentityReferences"/> are the strict-migration work queue. Each is
/// asserted with set equality and must count down to empty.</item>
/// </list>
///
/// See <c>docs/clean-architecture-plan.md</c> for the full history.
/// </summary>
internal static class KnownViolations
{
    /// <summary>
    /// Files outside Infrastructure that name an Entity Framework type directly and are not a
    /// composition root. The strict target is empty.
    ///
    /// <c>ThisCafeteria.Web</c> was cleared by plan Phases 2 and 3 and must stay clear.
    /// </summary>
    internal static readonly IReadOnlySet<string> DataAccessOutsideInfrastructure =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Emptied by strict plan Phase 1: Worker no longer owns EF adapters.</summary>
    internal static readonly IReadOnlySet<string> TemporaryWorkerDataAccess =
        new HashSet<string>(StringComparer.Ordinal)
        {
        };

    /// <summary>
    /// Razor components that perform data access instead of delegating to an injected service.
    ///
    /// Emptied by plan Phase 3: YieldPanel now injects IStakingLedgerService. Keep this empty.
    /// </summary>
    internal static readonly IReadOnlySet<string> DataAccessInRazorComponents =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Types implementing an Application interface from the wrong assembly. Keyed by the
    /// implementation's full type name.
    ///
    /// Emptied by plan Phase 1: all six moved into Infrastructure. Keep this set empty.
    /// </summary>
    internal static readonly IReadOnlySet<string> MisplacedApplicationImplementations =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Declared package dependencies that nothing in <c>src/</c> actually uses.
    ///
    /// Emptied by plan Phase 6: the MediatR reference was removed from Application, which had
    /// carried it with zero handlers and zero imports. Keep this empty.
    /// </summary>
    internal static readonly IReadOnlySet<string> UnusedArchitecturalPackages =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The only permanent source-level exceptions. Composition roots wire concrete types, and the
    /// Web design-time factory exists solely because EF tooling resolves it from the startup
    /// project. No application policy belongs here.
    /// </summary>
    internal static readonly IReadOnlySet<string> CompositionRoots =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // --- Composition roots. These exist precisely to know about concrete types. ---

            // Wires the DI container and registers the ASP.NET Identity EF store.
            "src/ThisCafeteria.Web/Program.cs",

            // IDesignTimeDbContextFactory, required by `dotnet ef` for migrations. It cannot live
            // in Infrastructure because the EF tooling resolves it from the startup project.
            "src/ThisCafeteria.Web/AppDbContextFactory.cs",

            // Wires the DI container for the background host.
            "src/ThisCafeteria.Worker/Program.cs",

        };

    /// <summary>Emptied by strict plan Phase 1: Worker no longer owns hosted adapters.</summary>
    internal static readonly IReadOnlySet<string> TemporaryWorkerHostedServices =
        new HashSet<string>(StringComparer.Ordinal)
        {
        };

    /// <summary>Emptied by strict plan Phase 1: RPC and Service Bus adapters live in Infrastructure.</summary>
    internal static readonly IReadOnlySet<string> TemporaryWorkerExternalAdapters =
        new HashSet<string>(StringComparer.Ordinal)
        {
        };

    /// <summary>Emptied by strict plan Phase 2: Web consumes IIdentityAccountService instead.</summary>
    internal static readonly IReadOnlySet<string> TemporaryWebIdentityReferences =
        new HashSet<string>(StringComparer.Ordinal)
        {
        };
}
