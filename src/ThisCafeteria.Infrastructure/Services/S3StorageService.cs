using Microsoft.Extensions.Logging;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class S3StorageService(ILogger<S3StorageService> logger) : IS3StorageService
{
    public Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("S3 upload placeholder for {FileName} with content type {ContentType}", fileName, contentType);
        return Task.FromResult($"s3://placeholder/{fileName}");
    }
}
