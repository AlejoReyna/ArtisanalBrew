namespace ThisCafeteria.Web.Models;

public sealed record WalletChallengeRequest(string Address);

public sealed record WalletChallengeResponse(
    string Message,
    string Nonce,
    int ChainId,
    string NetworkName,
    string RpcUrl,
    string ExplorerUrl);

public sealed record WalletVerifyRequest(
    string Address,
    string Signature,
    string Message,
    string Nonce,
    int ChainId);

public sealed record WalletVerifyResponse(
    bool Success,
    string Address,
    string RedirectUrl);
