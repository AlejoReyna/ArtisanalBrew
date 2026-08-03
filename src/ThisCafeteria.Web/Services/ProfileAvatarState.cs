using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Services;

/// <summary>
/// Per-circuit cache of the signed-in account's robot, so the header can show
/// it without querying on every navigation.
/// </summary>
/// <remarks>
/// Same shape as <c>WalletDashboardState</c>: load once, then let whoever
/// changes the value <see cref="Publish"/> it. Without the event the header
/// would keep the robot it read at sign-in and quietly disagree with /profile
/// for the rest of the session — the layout outlives the page that edits it.
///
/// The read runs in its own DI scope, which is not optional. The header lives
/// in the layout and initialises alongside whatever page is loading, so both
/// would otherwise issue EF work against the one <c>AppDbContext</c> the
/// circuit shares and blow up with "a second operation was started on this
/// context instance". It is a race, so it hides on an idle machine and shows
/// up under load — see YieldPanel, which takes an IDbContextFactory for the
/// same reason.
/// </remarks>
public sealed class ProfileAvatarState(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RobotAvatarDto? Current { get; private set; }

    /// <summary>Which account <see cref="Current"/> belongs to, so a different
    /// sign-in in the same circuit cannot inherit the previous robot.</summary>
    public string? ApplicationUserId { get; private set; }

    public event Action? Changed;

    public void Publish(string applicationUserId, RobotAvatarDto avatar)
    {
        ApplicationUserId = applicationUserId;
        Current = avatar;
        Changed?.Invoke();
    }

    public void Clear()
    {
        ApplicationUserId = null;
        Current = null;
        Changed?.Invoke();
    }

    public async Task<RobotAvatarDto?> GetOrLoadAsync(
        string? applicationUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return null;
        }

        if (Current is not null && ApplicationUserId == applicationUserId)
        {
            return Current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current is not null && ApplicationUserId == applicationUserId)
            {
                return Current;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var avatar = await scope.ServiceProvider
                .GetRequiredService<IProfileService>()
                .GetAvatarForApplicationUserAsync(applicationUserId, cancellationToken);

            ApplicationUserId = applicationUserId;
            Current = avatar;
            return avatar;
        }
        finally
        {
            _gate.Release();
        }
    }
}
