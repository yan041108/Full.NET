using System.Diagnostics;
using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>在真实导出仍有数据待传输时中止自有客户端，再以新的媒体恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiBackupInterruptedMySqlTests
{
    [TestMethod]
    public async Task Interrupted_export_drains_its_session_and_fresh_backup_restores_original_data()
    {
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_INTERRUPTED_BACKUP_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var source = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var target = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a06-interrupted-backup", stamp);
        var backup = Path.Combine(directory, "fresh.sql");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = "MySql", source, target, directory, backup }) + "\n");
        Directory.CreateDirectory(directory);
        var databases = new HashSet<string>(StringComparer.Ordinal) { source, target };
        var media = new HashSet<string>(StringComparer.Ordinal) { backup };
        var root = await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync();
        var builder = new MySqlConnectionStringBuilder(root) { Database = "mysql", Pooling = false };
        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        Assert.AreEqual(0L, await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME IN (@Source,@Target)", new { Source = source, Target = target }));
        await admin.ExecuteAsync($"CREATE DATABASE `{source}`");
        await admin.ExecuteAsync($"CREATE DATABASE `{target}`");
        await admin.ExecuteAsync($"CREATE TABLE `{source}`.backup_marker (Id INT PRIMARY KEY, Payload LONGTEXT NOT NULL)");
        var payload = new string('x', 1024 * 1024);
        await admin.ExecuteAsync($"INSERT INTO `{source}`.backup_marker (Id,Payload) VALUES (1,@Payload)", new { Payload = payload });

        using (var process = AiRecoveryBackupTransport.StartMySql("mysqldump", source, builder))
        {
            try
            {
                process.StandardInput.Close();
                var errors = process.StandardError.ReadToEndAsync();
                var bytes = new byte[4096];
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var received = await process.StandardOutput.BaseStream.ReadAsync(bytes, timeout.Token);
                Assert.IsGreaterThan(0, received, "必须先观察真实备份输出。");
                var observed = await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE DB=@Source", new { Source = source });
                Assert.IsGreaterThan(0L, observed, "必须先观察本测试源库的真实导出会话。");
                Assert.IsFalse(process.HasExited, "必须在备份进程仍运行时注入中断。");
                await AiRecoveryBackupTransport.StopOwnedProcessAsync(process);
                Assert.IsTrue(process.HasExited);
                Assert.AreNotEqual(0, process.ExitCode, "中断的备份不得冒充成功。");
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
        Assert.AreEqual(0L, sessions, "本次导出的服务端会话必须自然排空。");
        Assert.AreEqual(payload.Length, await admin.ExecuteScalarAsync<int>($"SELECT LENGTH(Payload) FROM `{source}`.backup_marker WHERE Id=1"));
        await AiRecoveryBackupTransport.ExportMySqlAsync(root, source, backup, databases, media, default);
        await AiRecoveryBackupTransport.ImportMySqlAsync(root, target, backup, databases, media, default);
        Assert.AreEqual(payload, await admin.ExecuteScalarAsync<string>($"SELECT Payload FROM `{target}`.backup_marker WHERE Id=1"));
    }
}
