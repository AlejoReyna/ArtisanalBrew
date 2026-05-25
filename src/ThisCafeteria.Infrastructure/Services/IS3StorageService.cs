namespace ThisCafeteria.Infrastructure.Services;

public interface IS3StorageService
{
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);
}
