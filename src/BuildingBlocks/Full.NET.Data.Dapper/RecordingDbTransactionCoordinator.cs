namespace Full.NET.Data.Dapper;

/// <summary>记录 Begin/Commit/Rollback 调用次数，供单元测试验证事务语义。</summary>
internal class RecordingDbTransactionCoordinator : IDbTransactionCoordinator
{
    public int BeginCount { get; protected set; }

    public int CommitCount { get; protected set; }

    public int RollbackCount { get; protected set; }

    public bool HasTransaction { get; protected set; }

    public virtual Task BeginAsync(CancellationToken cancellationToken)
    {
        if (HasTransaction)
        {
            throw new InvalidOperationException("A database transaction is already active.");
        }

        BeginCount++;
        HasTransaction = true;
        return Task.CompletedTask;
    }

    public virtual Task CommitAsync(CancellationToken cancellationToken)
    {
        if (!HasTransaction)
        {
            throw new InvalidOperationException("No database transaction is active.");
        }

        CommitCount++;
        HasTransaction = false;
        return Task.CompletedTask;
    }

    public virtual Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (!HasTransaction)
        {
            return Task.CompletedTask;
        }

        RollbackCount++;
        HasTransaction = false;
        return Task.CompletedTask;
    }
}