namespace ThisCafeteria.Infrastructure.Services;

public interface ISqsMessagePublisher
{
    Task<string?> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);
}
