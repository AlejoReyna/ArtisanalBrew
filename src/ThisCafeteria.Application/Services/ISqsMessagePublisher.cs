namespace ThisCafeteria.Application.Services;

public interface ISqsMessagePublisher
{
    Task<string?> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);
}
