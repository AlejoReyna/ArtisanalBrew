namespace ThisCafeteria.Application.Services;

/// <summary>
/// The canonical, bundler-independent confirmation of a mined UserOperation: the EntryPoint's own
/// <c>UserOperationEvent</c>, read directly from the chain node. This is the security-approved source
/// of truth — never a bundler's <c>success</c> flag — and it deliberately does not depend on the
/// bundler's <c>eth_getUserOperationReceipt</c> endpoint, which on a self-hosted node can fail even
/// after the operation is mined (Rundler's receipt lookup does an unbounded <c>eth_getLogs</c> the
/// node times out on, surfacing as <c>-32603 internal error: rpc provider error</c>).
/// </summary>
public interface IEntryPointConfirmationReader
{
    /// <summary>
    /// Locates the canonical <c>UserOperationEvent</c> for <paramref name="userOpHash"/> emitted by
    /// the trusted EntryPoint, matching <paramref name="sender"/> and the hash exactly, and reports
    /// whether the inner call succeeded. Returns <c>null</c> if no matching event is on-chain yet.
    ///
    /// <paramref name="transactionHashHint"/>, when supplied (e.g. from a bundler receipt), is
    /// verified directly against that transaction's own receipt; when absent — or when the bundler's
    /// receipt endpoint was unavailable — the event is located independently by an indexed-topic log
    /// query over a recent block window, so a mined operation is confirmed regardless of bundler
    /// receipt health.
    /// </summary>
    Task<EntryPointConfirmation?> FindConfirmationAsync(
        string chainKey,
        string sender,
        string userOpHash,
        string? transactionHashHint,
        CancellationToken cancellationToken = default);
}

/// <summary>A mined UserOperation confirmed from the canonical EntryPoint event.</summary>
public sealed record EntryPointConfirmation
{
    /// <summary>The transaction the EntryPoint mined this operation in.</summary>
    public required string TransactionHash { get; init; }

    /// <summary>The EntryPoint event's own <c>success</c> value for the inner call.</summary>
    public required bool Success { get; init; }
}
