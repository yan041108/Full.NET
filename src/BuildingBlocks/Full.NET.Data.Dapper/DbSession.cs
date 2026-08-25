using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 数据库会话（Database Session），非事务命令按次租用连接，显式事务持有唯一连接。
/// </summary>
/// <remarks>
/// <para>生命周期：Scoped，与业务请求/工作单元绑定；Dispose 时按事务→连接顺序释放资源。</para>
/// <para>线程安全：该类非线程安全，不应在并发异步流中共享同一实例。</para>
/// <para>事务隔离级别：默认使用 ReadCommitted，以避免脏读并平衡并发性能。</para>
/// </remarks>
internal sealed class DbSession(
    IDbConnectionFactory connectionFactory,
    DatabaseAdmissionGate admissionGate,
    DatabaseConnectionTelemetry telemetry,
    DatabaseAdmissionPriorityScope admissionPriority)
    : IAsyncDisposable, IDbTransactionCoordinator
{
    private DbTransaction? _transaction;
    private DbSessionConnectionLease? _transactionConnectionLease;

    /// <summary>
    /// 获取当前活动的数据库事务；若未开启事务则为 null。
    /// </summary>
    public DbTransaction? Transaction => _transaction;

    /// <summary>
    /// 获取一个值，指示当前会话是否已开启活动事务。
    /// </summary>
    public bool HasTransaction => _transaction is not null;

    /// <summary>
    /// 为一次命令获取连接租约；无事务时由租约拥有连接，有事务时只借用会话连接。
    /// </summary>
    /// <param name="cancellationToken">用于取消打开连接操作的令牌。</param>
    /// <returns>包含已打开连接和当前事务的异步租约。</returns>
    public async Task<DbSessionConnectionLease> AcquireConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            return DbSessionConnectionLease.CreateBorrowed(
                _transactionConnectionLease!.Connection,
                _transaction);
        }

        return await OpenOwnedConnectionAsync(cancellationToken).ConfigureAwait(false);
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

        var connectionLease = await OpenOwnedConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var transaction = await connectionLease.Connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            connectionLease.Transaction = transaction;
            _transactionConnectionLease = connectionLease;
            _transaction = transaction;
        }
        catch
        {
            await connectionLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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
        _transaction = null;
        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await ReleaseTransactionConnectionAsync().ConfigureAwait(false);
        }
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

        _transaction = null;
        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await ReleaseTransactionConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 异步释放当前会话持有的事务与连接资源。
    /// </summary>
    /// <remarks>
    /// 释放顺序：先 DbTransaction（若未提交/回滚将隐式回滚），后事务连接租约。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try
        {
            var transaction = _transaction;
            _transaction = null;
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await ReleaseTransactionConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task<DbSessionConnectionLease> OpenOwnedConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = connectionFactory.Create();
        DatabaseAdmissionLease? admissionLease = null;
        var acquisitionStartedAt = Stopwatch.GetTimestamp();
        try
        {
            admissionLease = admissionPriority.IsCritical
                ? await admissionGate
                    .AcquireCriticalAsync(cancellationToken)
                    .ConfigureAwait(false)
                : await admissionGate
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(false);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                telemetry.RecordAcquisition(
                    DatabaseConnectionAcquireOutcome.Canceled,
                    Stopwatch.GetElapsedTime(acquisitionStartedAt));
                throw;
            }
            catch
            {
                telemetry.RecordAcquisition(
                    DatabaseConnectionAcquireOutcome.Failure,
                    Stopwatch.GetElapsedTime(acquisitionStartedAt));
                throw;
            }

            telemetry.RecordAcquisition(
                DatabaseConnectionAcquireOutcome.Success,
                Stopwatch.GetElapsedTime(acquisitionStartedAt));
            return DbSessionConnectionLease.CreateOwned(
                connection,
                admissionLease,
                telemetry,
                Stopwatch.GetTimestamp());
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            if (admissionLease is not null)
            {
                await admissionLease.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async ValueTask ReleaseTransactionConnectionAsync()
    {
        var connectionLease = _transactionConnectionLease;
        _transactionConnectionLease = null;
        if (connectionLease is not null)
        {
            await connectionLease.DisposeAsync().ConfigureAwait(false);
        }
    }
}
