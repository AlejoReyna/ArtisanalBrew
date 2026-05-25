namespace ThisCafeteria.Web.Configuration;

/// <summary>
/// Owner credentials for CoffeeCoin mint. Store the private key in User Secrets or environment variables — never in appsettings committed to git.
/// </summary>
public sealed class CoffeeCoinOwnerOptions
{
    public const string SectionName = "CoffeeCoinOwner";

    /// <summary>Hex private key of the CoffeeCoin contract owner (with or without 0x prefix).</summary>
    public string? PrivateKey { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PrivateKey);
}
