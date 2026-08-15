using System.Data;
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 数据库会话（Database Session），持有当前 Scope 内唯一的 DbConnection 与 DbTransaction。
/// </summary>
/// <remarks>
/// <para>生命周期：Scoped，与业务请求/工作单元绑定；Dispose 时按事务→连接顺序释放资源。</para>
/// <para>线程安全：该类非线程安全，不应在并发异步流中共享同一实例。</para>
/// <para>事务隔离级别：默认使用 ReadCommitted，以避免脏读并平衡并发性能。</para>
/// </remarks>
internal sealed class DbSession(DbConnectionFactory connectionFactory)
    : IAsyncDisposable, IDbTransactionCoordinator
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    /// <summary>
    /// 获取当前活动的数据库事务；若未开启事务则为 null。
    /// </summary>
    public DbTransaction? Transaction => _transaction;

    /// <summary>
    /// 获取一个值，指示当前会话是否已开启活动事务。
    /// </summary>
    public bool HasTransaction => _transaction is not null;

    /// <summary>
    /// 获取已打开的数据库连接；若连接尚未创建或已关闭，则惰性创建并打开。
    /// </summary>
    /// <param name="cancellationToken">用于取消打开连接操作的令牌。</param>
    /// <returns>已打开的 DbConnection 实例。</returns>
    public async Task<DbConnection> GetOpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        _connection ??= connectionFactory.Create();
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        return _connection;
    }

    /// <summary>
    /// 以 ReadCommitted 隔离级别异步开启新的数据库事务。
    /// </summary>
    /// <param name="cancellationToken">用于取消开启事务操作的令牌。</param>
    /// <exception cref="InvalidOperationException">当当前会话已存在活动事务时抛出。</exception>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A database transaction is already active.");
        }

        var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 异步提交当前活动的数据库事务，并释放事务资源。
    /// </summary>
    /// <param name="cancellationToken">用于取消提交操作的令牌。</param>
    /// <exception cref="InvalidOperationException">当当前会话无活动事务时抛出。</exception>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No database transaction is active.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    /// <summary>
    /// 异步回滚当前活动的数据库事务（若存在），并释放事务资源。
    /// </summary>
    /// <param name="cancellationToken">用于取消回滚操作的令牌。</param>
    /// <remarks>无活动事务时为 no-op，允许在不确定状态下安全重复调用。</remarks>
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    /// <summary>
    /// 异步释放当前会话持有的事务与连接资源。
    /// </summary>
    /// <remarks>
    /// 释放顺序：先 DbTransaction（若未提交/回滚将隐式回滚），后 DbConnection。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
