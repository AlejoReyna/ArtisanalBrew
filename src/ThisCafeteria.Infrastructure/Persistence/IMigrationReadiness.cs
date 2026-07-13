namespace ThisCafeteria.Infrastructure.Persistence;

public interface IMigrationReadiness
{
    bool IsReady { get; }
    Task ReadyTask { get; }
    void MarkReady();
}
