namespace ThisCafeteria.Infrastructure.Persistence;

public interface IMigrationReadiness
{
    bool IsReady { get; }
    Exception? Failure { get; }
    void MarkReady();
    void MarkFailed(Exception exception);
}
