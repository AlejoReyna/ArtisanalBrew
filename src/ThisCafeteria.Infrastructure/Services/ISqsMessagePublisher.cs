namespace ThisCafeteria.Infrastructure.Services;

public interface ISqsMessagePublisher
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);
}
