namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class MigrationReadiness : IMigrationReadiness
{
    private readonly TaskCompletionSource _completionSource = new();

    public bool IsReady => _completionSource.Task.IsCompleted;

    public Task ReadyTask => _completionSource.Task;

    public void MarkReady() => _completionSource.TrySetResult();
}
