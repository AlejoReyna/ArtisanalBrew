using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class LocalReceiptStorageService(
    IOptions<ReceiptStorageOptions> options,
    ILogger<LocalReceiptStorageService> logger) : IS3StorageService
{
    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var storagePath = options.Value.StoragePath;
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new InvalidOperationException("Receipts:StoragePath is not configured.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !string.Equals(safeFileName, fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Receipt file name must not contain a path.", nameof(fileName));
        }

        var directory = Path.GetFullPath(storagePath);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, safeFileName);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        logger.LogInformation(
            "Stored receipt {FileName} locally with content type {ContentType}",
            safeFileName,
            contentType);
        return new Uri(destination).AbsoluteUri;
    }
}
