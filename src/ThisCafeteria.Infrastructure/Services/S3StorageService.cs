using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

// Backs the AWS-flavored IS3StorageService contract with Azure Blob Storage.
public sealed class S3StorageService(
    IOptions<AzureOptions> options,
    TokenCredential credential,
    ILogger<S3StorageService> logger) : IS3StorageService
{
    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var storageOptions = options.Value.Storage;
        if (string.IsNullOrWhiteSpace(storageOptions.BlobEndpoint))
        {
            throw new InvalidOperationException("Azure:Storage:BlobEndpoint is not configured.");
        }

        var serviceClient = new BlobServiceClient(new Uri(storageOptions.BlobEndpoint), credential);
        var containerClient = serviceClient.GetBlobContainerClient(storageOptions.ReceiptsContainerName);
        var blobClient = containerClient.GetBlobClient(fileName);

        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Uploaded blob {FileName} to container {Container}",
            fileName,
            storageOptions.ReceiptsContainerName);

        return blobClient.Uri.ToString();
    }
}
