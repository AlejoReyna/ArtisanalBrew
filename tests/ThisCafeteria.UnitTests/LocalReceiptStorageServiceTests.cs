using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Services;

namespace ThisCafeteria.UnitTests;

public sealed class LocalReceiptStorageServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"artisanalbrew-receipts-{Guid.NewGuid():N}");

    [Fact]
    public async Task UploadAsync_WritesReceiptAtomically()
    {
        var storage = new LocalReceiptStorageService(
            Options.Create(new ReceiptStorageOptions { StoragePath = _directory }),
            NullLogger<LocalReceiptStorageService>.Instance);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var uri = await storage.UploadAsync(content, "order-123.pdf", "application/pdf");

        uri.Should().StartWith("file:");
        File.ReadAllBytes(Path.Combine(_directory, "order-123.pdf")).Should().Equal(1, 2, 3, 4);
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_RejectsPathTraversal()
    {
        var storage = new LocalReceiptStorageService(
            Options.Create(new ReceiptStorageOptions { StoragePath = _directory }),
            NullLogger<LocalReceiptStorageService>.Instance);
        await using var content = new MemoryStream([1]);

        var action = () => storage.UploadAsync(content, "../receipt.pdf", "application/pdf");

        await action.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
