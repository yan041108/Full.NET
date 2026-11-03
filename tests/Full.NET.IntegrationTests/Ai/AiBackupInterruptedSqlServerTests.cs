using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅中止本测试拥有的备份命令，并以新媒体恢复原库数据。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiBackupInterruptedSqlServerTests
{
    [TestMethod]
    public async Task Interrupted_backup_drains_its_request_and_fresh_backup_restores_original_data()
    {
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_INTERRUPTED_BACKUP_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var source = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var target = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var interrupted = "/var/opt/mssql/data/a06_recovery_" + Guid.CreateVersion7().ToString("N") + ".bak";
        var fresh = "/var/opt/mssql/data/a06_recovery_" + Guid.CreateVersion7().ToString("N") + ".bak";
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = "SqlServer", source, target, interrupted, backup = fresh }) + "\n");
        var databases = new HashSet<string>(StringComparer.Ordinal) { source, target };
        var media = new HashSet<string>(StringComparer.Ordinal) { interrupted, fresh };
        var master = await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync();
        await using var admin = new SqlConnection(new SqlConnectionStringBuilder(master) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name IN (@Source,@Target)", new { Source = source, Target = target }));
        await admin.ExecuteAsync($"CREATE DATABASE [{source}]");
        await admin.ExecuteAsync($"CREATE TABLE [{source}].dbo.backup_marker (Id INT NOT NULL PRIMARY KEY, Payload VARBINARY(MAX) NOT NULL)");
        // 使用足够多的已提交数据让观察者有机会看到真实 BACKUP 请求，不依赖任意延时推断备份开始。
        await admin.ExecuteAsync($"INSERT INTO [{source}].dbo.backup_marker (Id,Payload) SELECT TOP (8192) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CONVERT(VARBINARY(MAX),REPLICATE(CONVERT(VARCHAR(MAX),'a'),8000)) FROM sys.all_objects a CROSS JOIN sys.all_objects b", commandTimeout: 180);
        var original = await admin.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{source}].dbo.backup_marker");
        Assert.AreEqual(8192L, original);

        await using var backupConnection = new SqlConnection(new SqlConnectionStringBuilder(master) { Pooling = false }.ConnectionString);
        await backupConnection.OpenAsync();
        var session = await backupConnection.ExecuteScalarAsync<int>("SELECT @@SPID");
        using var cancel = new CancellationTokenSource();
        var operation = AiRecoveryBackupTransport.BackupSqlServerAsync(backupConnection, source, interrupted, databases, media, cancel.Token);
        try
        {
            var timer = Stopwatch.StartNew();
            var observed = 0L;
            while (timer.Elapsed < TimeSpan.FromSeconds(30) && !operation.IsCompleted && observed == 0)
            {
                observed = await admin.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE session_id=@Session AND command='BACKUP DATABASE'", new { Session = session });
                if (observed == 0) await Task.Delay(10);
            }
            Assert.IsGreaterThan(0L, observed, "必须先观察本测试会话实际执行 BACKUP DATABASE。");
            cancel.Cancel();
            var interruptedAsExpected = false;
            try { await operation; }
            catch (OperationCanceledException) { interruptedAsExpected = true; }
            catch (SqlException) when (cancel.IsCancellationRequested) { interruptedAsExpected = true; }
            Assert.IsTrue(interruptedAsExpected, "中断的备份不得冒充成功。");
        }
        finally
        {
            await cancel.CancelAsync();
            try { await operation; } catch (OperationCanceledException) { } catch (SqlException) { }
        }
        var deadline = Stopwatch.StartNew();
        var pending = -1L;
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            pending = await admin.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE session_id=@Session", new { Session = session });
            if (pending == 0) break;
            await Task.Delay(100);
        }
        Assert.AreEqual(0L, pending, "本测试备份请求必须排空。");
        Assert.AreEqual(original, await admin.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{source}].dbo.backup_marker"));
        await AiRecoveryBackupTransport.BackupSqlServerAsync(admin, source, fresh, databases, media, default);
        await AiRecoveryBackupTransport.RestoreSqlServerAsync(admin, target, fresh, databases, media, default);
        Assert.AreEqual(original, await admin.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{target}].dbo.backup_marker WHERE DATALENGTH(Payload)=8000"));
    }
}
