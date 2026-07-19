using System.Text.Json;

namespace ThisCafeteria.Web.Models;

public sealed record WalletChallengeRequest(string Address, string? WalletName = null, string? ChainKey = null);

public sealed record WalletChallengeResponse(
    string Message,
    string Nonce,
    int ChainId,
    string ChainIdHex,
    string NetworkName,
    string RpcUrl,
    string ExplorerUrl,
    string CurrencyName,
    string CurrencySymbol,
    int CurrencyDecimals,
    string ChainKey = "ethereum-sepolia",
    string Family = "Evm");

public sealed record WalletVerifyRequest(
    string Address,
    string Signature,
    string Message,
    string Nonce,
    int ChainId,
    string? WalletName = null,
    string? ChainKey = null);

public sealed record SolanaWalletChallengeRequest(string Address, string ChainKey, string? WalletName = null);
public sealed record SolanaWalletChallengeResponse(string Message, string Nonce, string ChainKey, string Cluster, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
public sealed record SolanaWalletVerifyRequest(string Address, string Signature, string Message, string Nonce, string ChainKey, string? WalletName = null);

public sealed record WalletVerifyResponse(
    bool Success,
    string Address,
    string RedirectUrl,
    bool StatusStored,
    bool StatusPublished,
    string? AwsMessageId);

public sealed record WalletStatusMessage(
    string WalletAddress,
    string Status,
    string? EventType,
    object? Payload,
    DateTimeOffset CreatedAt);

public sealed record WalletStatusRequest(
    string WalletAddress,
    string Status,
    string? EventType = null,
    JsonElement? Payload = null);

public sealed record WalletStatusResponse(
    Guid Id,
    string WalletAddress,
    string Status,
    string? EventType,
    JsonElement? Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedToAwsAtUtc,
    string? AwsMessageId);

public sealed record WalletStatusCreateResponse(
    Guid Id,
    string WalletAddress,
    string Status,
    string? EventType,
    DateTimeOffset CreatedAt,
    bool PublishedToAws,
    string? AwsMessageId);
