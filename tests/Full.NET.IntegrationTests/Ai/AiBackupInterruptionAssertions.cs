using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只中断本测试登记的备份请求，等待其退出后再使用新的媒体。</summary>
internal static class AiBackupInterruptionAssertions
{
    internal static async Task InterruptSqlServerAsync(SqlConnection admin, string master, string source, string interrupted,
        IReadOnlySet<string> databases, IReadOnlySet<string> media)
    {
        await admin.ExecuteAsync($"CREATE TABLE [{source}].dbo.backup_interrupt_marker (Id INT NOT NULL PRIMARY KEY, Payload VARBINARY(MAX) NOT NULL)");
        await admin.ExecuteAsync($"INSERT INTO [{source}].dbo.backup_interrupt_marker (Id,Payload) SELECT TOP (8192) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), CONVERT(VARBINARY(MAX),REPLICATE(CONVERT(VARCHAR(MAX),'a'),8000)) FROM sys.all_objects a CROSS JOIN sys.all_objects b", commandTimeout: 180);
        Assert.AreEqual(8192L, await admin.ExecuteScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{source}].dbo.backup_interrupt_marker"));
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(master) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        var session = await connection.ExecuteScalarAsync<int>("SELECT @@SPID");
        using var cancel = new CancellationTokenSource();
        var operation = AiRecoveryBackupTransport.BackupSqlServerAsync(connection, source, interrupted, databases, media, cancel.Token);
        try
        {
            var watch = Stopwatch.StartNew();
            var observed = 0L;
            while (watch.Elapsed < TimeSpan.FromSeconds(30) && !operation.IsCompleted && observed == 0)
            {
                observed = await admin.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE session_id=@Session AND command='BACKUP DATABASE'", new { Session = session });
                if (observed == 0) await Task.Delay(10);
            }
            Assert.IsGreaterThan(0L, observed, "必须先观察本测试会话实际执行备份。");
            cancel.Cancel();
            var canceled = false;
            try { await operation; }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { Console.WriteLine("A06_INTERRUPTED_SQL_RESULT=OperationCanceledException"); canceled = true; }
            catch (SqlException error) when (cancel.IsCancellationRequested) { Console.WriteLine($"A06_INTERRUPTED_SQL_RESULT=SqlException:{error.Number}"); canceled = true; }
            Assert.IsTrue(canceled, "中断的备份不得计为成功。");
        }
        finally
        {
            await cancel.CancelAsync();
            try { await operation; } catch (OperationCanceledException) { } catch (SqlException) { }
        }
        var timer = Stopwatch.StartNew();
        var pending = -1L;
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            pending = await admin.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE session_id=@Session", new { Session = session });
            if (pending == 0) break;
            await Task.Delay(100);
        }
        Assert.AreEqual(0L, pending, "被取消的备份请求必须排空。");
    }

    internal static async Task InterruptMySqlAsync(MySqlConnection admin, string root, string source)
    {
        await admin.ExecuteAsync($"CREATE TABLE `{source}`.backup_interrupt_marker (Id INT PRIMARY KEY, Payload LONGTEXT NOT NULL)");
        await admin.ExecuteAsync($"INSERT INTO `{source}`.backup_interrupt_marker (Id,Payload) VALUES (1,@Payload)", new { Payload = new string('x', 1024 * 1024) });
        using (var process = AiRecoveryBackupTransport.StartMySql("mysqldump", source, new MySqlConnectionStringBuilder(root)))
        {
            try
            {
                process.StandardInput.Close();
                var errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var received = await process.StandardOutput.BaseStream.ReadAsync(new byte[4096], timeout.Token);
                Assert.IsGreaterThan(0, received, "必须先观察真实备份输出。");
                var active = await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE DB=@Source", new { Source = source });
                Assert.IsGreaterThan(0L, active, "必须先观察源库导出会话。");
                Assert.IsFalse(process.HasExited, "必须在自有进程退出前注入中断。");
                await AiRecoveryBackupTransport.StopOwnedProcessAsync(process);
                Assert.AreNotEqual(0, process.ExitCode, "中断的导出不得计为成功。");
                Console.WriteLine($"A06_INTERRUPTED_MYSQL_EXIT={process.ExitCode}");
                _ = await errors;
            }
            finally { await AiRecoveryBackupTransport.StopOwnedProcessAsync(process); }
        }
        var timer = Stopwatch.StartNew();
        var sessions = -1L;
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            sessions = await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE DB=@Source", new { Source = source });
            if (sessions == 0) break;
            await Task.Delay(100);
        }
        Assert.AreEqual(0L, sessions, "本次导出会话必须自然排空。");
    }
}
