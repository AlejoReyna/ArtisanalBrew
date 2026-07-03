using Azure.Core;
using Azure.Identity;

namespace ThisCafeteria.Infrastructure.Configuration;

internal static class AzureClientFactory
{
    // DefaultAzureCredential tries ManagedIdentityCredential first (using the configured
    // client ID when the Container App's user-assigned identity would otherwise be
    // ambiguous), then falls through to AzureCliCredential — which is what lets this same
    // code authenticate locally against real Azure resources for a developer who has run
    // `az login`, without any separate local-vs-production branching.
    public static TokenCredential CreateCredential(AzureManagedIdentityOptions options) =>
        string.IsNullOrWhiteSpace(options.ClientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ClientId });
}
