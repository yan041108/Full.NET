using System.Data;
using System.Data.Common;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>仅锁定本用例数据库的审计表，验证真实驱动取消、整批终结及释放锁后的恢复。</summary>
internal static class AuditingBatchFaultAssertions
{
    internal enum Failure { BatchDeadline, CallerCancellation, ConnectionLost }

    public static async Task VerifyAsync(FullNetApiFactory factory, Failure failure)
    {
        // 冷模板克隆属于准备阶段，由外层测试组限制；故障预算从数据库就绪后开始。
        await factory.InitializeAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var commands = new ObservedCommands(scope.ServiceProvider.GetRequiredService<ICommandExecutor>(),
            scope.ServiceProvider.GetRequiredService<IQueryExecutor>(), factory.Provider);
        var writer = ActivatorUtilities.CreateInstance<AuditWriteBatchWriter>(scope.ServiceProvider, commands);
        var trace = "b1-blocked-" + Guid.NewGuid().ToString("N");
        var rows = new[] { Create(trace), Create(trace) };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var blocker = await TableBlocker.AcquireAsync(factory, token);
        Task? writing = null;
        try
        {
            writing = writer.WriteMicroBatchAsync(rows, caller.Token);
            await commands.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            // 观察服务端真实锁等待，不能仅凭任务未完成就声称命令已到达数据库。
            var blockedSessionId = await blocker.WaitForBlockedInsertAsync(factory, commands.SessionId, token);
            Assert.IsFalse(writing.IsCompleted, "写入必须确实被本用例的表锁阻塞。");
            if (failure == Failure.CallerCancellation)
            {
                await caller.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => writing.WaitAsync(TimeSpan.FromSeconds(15), token));
            }
            else
            {
                if (failure == Failure.ConnectionLost)
                    await blocker.DisconnectWriterAsync(factory, blockedSessionId, token);
                await writing.WaitAsync(TimeSpan.FromSeconds(15), token);
            }

            Assert.AreEqual(1, commands.Attempts, "锁等待/取消不能触发毒记录二分或自动重放。");
            Assert.AreEqual(0, commands.Active, "写入返回时实际数据库调用必须已退出。");
            foreach (var row in rows)
            {
                Assert.IsTrue(row.Completion.Task.IsCompletedSuccessfully, "所有信封都必须终结。");
                var result = await row.Completion.Task;
                Assert.IsFalse(result.Succeeded);
                Assert.IsFalse(result.Poisoned, "基础设施故障不能把健康记录标成毒记录。");
            }
        }
        finally
        {
            // 失败断言也先取消并释放自有锁，再等实际操作退出，避免留下后台写入。
            await caller.CancelAsync();
            await blocker.DisposeAsync();
            if (writing is not null)
            {
                try { await writing.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
            }
        }

        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        async Task<long> CountAsync(string value) => await queries.QuerySingleOrDefaultAsync<long>(
            new SqlStatement("test.auditing.count_b1_after_lock_cancellation",
                "SELECT COUNT(*) FROM fn_auditing_operation_log WHERE TraceId = @TraceId", SqlDataScope.Global),
            new { TraceId = value }, token);
        Assert.AreEqual(0L, await CountAsync(trace), "释放锁后不能出现超时批次的迟到写入。");
        var recoveredTrace = "b1-recovered-" + Guid.NewGuid().ToString("N");
        var recovered = Create(recoveredTrace);
        await writer.WriteMicroBatchAsync([recovered], token);
        Assert.IsTrue((await recovered.Completion.Task).Succeeded, "故障解除后同一写入边界必须恢复。");
        Assert.AreEqual(1L, await CountAsync(recoveredTrace));
        Assert.AreEqual(0L, await CountAsync(trace));
        Assert.AreEqual(2, commands.Attempts);
        Assert.AreEqual(0, commands.Active);
    }

    private static AuditWriteEnvelope Create(string trace) => AuditWriteEnvelope.ForOperation(
        new OperationLogWriteModel("auditing.blocked.probe", "POST", "/api/v1/auditing/blocked-probe",
            200, 1, true, null, null, trace, null, "auditing.blocked.probe"));

    private sealed class ObservedCommands(ICommandExecutor inner, IQueryExecutor queries, DatabaseProvider provider) : ICommandExecutor
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts;
        public int Active;
        public long SessionId;

        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            Interlocked.Increment(ref Active);
            try
            {
                // Writer 已开启事务，此查询借用同一连接；目标 ID 不能从其他后台命令猜测。
                SessionId = await queries.QuerySingleOrDefaultAsync<long>(
                    new SqlStatement("test.auditing.b1_writer_session",
                        provider == DatabaseProvider.SqlServer ? "SELECT CAST(@@SPID AS bigint)" : "SELECT CONNECTION_ID()",
                        SqlDataScope.Global), cancellationToken: cancellationToken);
                Started.TrySetResult();
                return await inner.ExecuteAsync(statement, parameters, cancellationToken);
            }
            catch (Exception exception)
            {
                Started.TrySetException(exception);
                throw;
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }

    /// <summary>独立连接持锁；不停止共享数据库服务，不修改权限或其他用例的表。</summary>
    internal sealed class TableBlocker(DbConnection connection, DbTransaction? transaction, long sessionId) : IAsyncDisposable
    {
        private static DbConnection NewConnection(FullNetApiFactory factory) => factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(new SqlConnectionStringBuilder(factory.ConnectionString) { Pooling = false }.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(factory.ConnectionString) { Pooling = false }.ConnectionString);

        public static async Task<TableBlocker> AcquireAsync(FullNetApiFactory factory, CancellationToken token)
        {
            var connection = NewConnection(factory);
            DbTransaction? transaction = null;
            try
            {
                await connection.OpenAsync(token);
                await using var id = connection.CreateCommand();
                id.CommandText = factory.Provider == DatabaseProvider.SqlServer ? "SELECT @@SPID" : "SELECT CONNECTION_ID()";
                var sessionId = Convert.ToInt64(await id.ExecuteScalarAsync(token));
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 10;
                if (factory.Provider == DatabaseProvider.SqlServer)
                {
                    transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    command.Transaction = transaction;
                    command.CommandText = "SELECT COUNT_BIG(*) FROM fn_auditing_operation_log WITH (TABLOCKX, HOLDLOCK)";
                    await command.ExecuteScalarAsync(token);
                }
                else
                {
                    command.CommandText = "LOCK TABLES fn_auditing_operation_log WRITE";
                    await command.ExecuteNonQueryAsync(token);
                }
                return new(connection, transaction, sessionId);
            }
            catch
            {
                if (transaction is not null) await transaction.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async Task<long> WaitForBlockedInsertAsync(FullNetApiFactory factory, long writerSessionId, CancellationToken token)
        {
            await using var observer = NewConnection(factory);
            await observer.OpenAsync(token);
            await using var command = observer.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = factory.Provider == DatabaseProvider.SqlServer
                ? "SELECT TOP (1) r.session_id FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.database_id = DB_ID() AND r.session_id = @Writer AND r.blocking_session_id = @Blocker AND t.text LIKE '%INSERT INTO fn_auditing_operation_log%'"
                : "SELECT ID FROM information_schema.PROCESSLIST WHERE DB = DATABASE() AND ID = @Writer AND STATE LIKE '%lock%' AND INFO LIKE '%INSERT INTO fn_auditing_operation_log%' LIMIT 1";
            var writerParameter = command.CreateParameter();
            writerParameter.ParameterName = "@Writer";
            writerParameter.Value = writerSessionId;
            command.Parameters.Add(writerParameter);
            if (factory.Provider == DatabaseProvider.SqlServer)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@Blocker";
                parameter.Value = sessionId;
                command.Parameters.Add(parameter);
            }
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(token);
            observation.CancelAfter(TimeSpan.FromSeconds(4));
            while (true)
            {
                var blocked = Convert.ToInt64(await command.ExecuteScalarAsync(observation.Token));
                if (blocked > 0) return blocked;
                await Task.Delay(25, observation.Token);
            }
        }

        public async Task DisconnectWriterAsync(FullNetApiFactory factory, long blockedSessionId, CancellationToken token)
        {
            // 只终止刚刚在当前临时库、被自有锁阻塞的 INSERT 会话，不影响共享服务。
            Assert.IsTrue(blockedSessionId > 0 && blockedSessionId <= int.MaxValue && blockedSessionId != sessionId);
            await using var observer = NewConnection(factory);
            await observer.OpenAsync(token);
            await using var command = observer.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = factory.Provider == DatabaseProvider.SqlServer
                ? $"KILL {blockedSessionId}"
                : $"KILL CONNECTION {blockedSessionId}";
            await command.ExecuteNonQueryAsync(token);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (transaction is not null)
                {
                    try { await transaction.RollbackAsync(); }
                    finally { await transaction.DisposeAsync(); }
                }
                else
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "UNLOCK TABLES";
                    command.CommandTimeout = 5;
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
