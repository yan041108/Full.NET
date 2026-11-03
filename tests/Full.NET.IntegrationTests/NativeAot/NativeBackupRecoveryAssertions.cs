using System.Data.Common;
using System.Diagnostics;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>恢复库上的实际 Worker 经独立代理经历数据库连接故障，再沿原回执自动收敛。</summary>
internal static class NativeBackupRecoveryAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connection, string keys, string files,
        IReadOnlyList<(Guid Run, Guid? Tenant)> runs)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Fail(reason ?? "缺少 Native Worker");
        await using var proxy = NativeDatabaseFaultProxy.Start(provider, connection, out var workerConnection);
        await using var blocker = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        var blockerId = await blocker.ExecuteScalarAsync<long>(provider == DatabaseProvider.SqlServer ? "SELECT @@SPID" : "SELECT CONNECTION_ID()", transaction: transaction);
        var lockSql = provider == DatabaseProvider.SqlServer
            ? "SELECT Id FROM fn_ai_agent_run WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id AND StatusKey='reconciliation_required'"
            : "SELECT Id FROM fn_ai_agent_run WHERE Id=@Id AND StatusKey='reconciliation_required' FOR UPDATE";
        var versions = new Dictionary<Guid, long>();
        foreach (var (run, _) in runs)
        {
            Assert.AreEqual(run, await blocker.QuerySingleAsync<Guid>(lockSql, new { Id = run }, transaction));
            versions[run] = await blocker.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }, transaction);
        }
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = keys,
            ["FullNet:Ai:AgentRuntime:ReconcileCompletedReceipts"] = "true",
            ["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "500",
        };
        await using var worker = await NativeWorkerProcessHost.StartAsync(artifact!, provider, workerConnection,
            NativeAotTestTimeouts.ProcessStartup, filesRootPath: files, additionalSettings: settings);
        Console.WriteLine($"A06_NATIVE_BACKUP_WORKER_PID={worker.ProcessId}");
        await using DbConnection probe = provider == DatabaseProvider.SqlServer
            ? Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(true, connection)
            : new MySqlConnection(new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()) { Pooling = false }.ConnectionString);
        await probe.OpenAsync();
        var waitSql = provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@Id"
            : "SELECT COUNT(*) FROM performance_schema.data_lock_waits w JOIN performance_schema.threads t ON w.BLOCKING_THREAD_ID=t.THREAD_ID WHERE t.PROCESSLIST_ID=@Id";
        var timer = Stopwatch.StartNew();
        var waiting = 0L;
        while (timer.Elapsed < TimeSpan.FromSeconds(60) && waiting == 0)
        {
            waiting = await probe.ExecuteScalarAsync<long>(waitSql, new { Id = blockerId });
            if (waiting == 0) await Task.Delay(100);
        }
        Assert.IsGreaterThan(0L, waiting, "故障前须观测本测试根行锁阻塞数据库会话。");
        Assert.IsGreaterThan(0, proxy.AcceptedConnections, "Worker 必须经本测试代理连接真实数据库。");
        var acceptedBeforeCut = proxy.PauseAndCut();
        Assert.IsGreaterThan(0, proxy.CutConnections, "必须实际切断 Worker 的至少一个 TCP 连接。");
        await proxy.WaitForDrainAsync(TimeSpan.FromSeconds(10));
        await transaction.RollbackAsync();
        await using var verify = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var (run, _) in runs)
        {
            Assert.AreEqual("reconciliation_required", await verify.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
            Assert.AreEqual(versions[run], await verify.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND UsageStatus='unknown'", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
        }
        proxy.Resume();
        timer.Restart();
        while (timer.Elapsed < TimeSpan.FromSeconds(75))
        {
            var reconciled = 0;
            foreach (var (run, _) in runs)
                reconciled += await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND UsageStatus='known' AND InputTokens=2 AND OutputTokens=1", new { Id = run });
            if (reconciled == runs.Count) break;
            await Task.Delay(200);
        }
        foreach (var (run, _) in runs)
        {
            Assert.AreEqual("failed", await verify.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }), worker.LogFilePath);
            Assert.AreEqual(versions[run] + 1, await verify.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND RunId=@Id AND UsageStatus='known' AND InputTokens=2 AND OutputTokens=1", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE RunId=@Id", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_checkpoint WHERE RunId=@Id", new { Id = run }));
            Assert.AreEqual(1L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id AND EventType='run.automatically_reconciled'", new { Id = run }));
            Assert.AreEqual(3L, await verify.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
        }
        Assert.IsGreaterThan(acceptedBeforeCut, proxy.AcceptedConnections, "恢复后必须有新的真实数据库连接。");
        await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(0, worker.ExitCode);
        var log = await File.ReadAllTextAsync(worker.LogFilePath);
        foreach (var fatal in new[] { "MissingMethodException", "TypeInitializationException", "JsonSerializerIsReflectionDisabled", "Unhandled exception" })
            Assert.IsFalse(log.Contains(fatal, StringComparison.Ordinal), "Native Worker 异常：" + fatal);
        Console.WriteLine($"A06_NATIVE_BACKUP_PROXY cut={proxy.CutConnections} accepted={proxy.AcceptedConnections}");
    }
}
