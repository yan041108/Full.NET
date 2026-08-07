namespace Full.NET.Data.Dapper;

internal interface IDbTransactionCoordinator
{
    bool HasTransaction { get; }

    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}