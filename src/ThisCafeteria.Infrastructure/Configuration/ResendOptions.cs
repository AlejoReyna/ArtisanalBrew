namespace ThisCafeteria.Infrastructure.Configuration;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "ArtisanalBrew";
}

public sealed class ReceiptStorageOptions
{
    public const string SectionName = "Receipts";

    public string StoragePath { get; set; } = "/data/receipts";
}
