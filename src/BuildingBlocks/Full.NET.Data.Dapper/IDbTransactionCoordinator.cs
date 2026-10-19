namespace Full.NET.Data.Dapper;

/// <summary>
/// 单连接上的显式事务协调器；命令执行器在同一协调器上串行提交或回滚，禁止嵌套本地事务。
/// </summary>
internal interface IDbTransactionCoordinator
{
    bool HasTransaction { get; }

    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}