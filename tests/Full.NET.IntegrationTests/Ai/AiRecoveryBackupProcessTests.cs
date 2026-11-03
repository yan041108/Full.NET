using Dapper;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>取消只能终止本批拥有的本地客户端，并等待其数据库会话自然排空。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiRecoveryBackupProcessTests
{
    [TestMethod]
    public async Task Owned_mysql_client_exit_and_server_session_drain()
    {
        var root = new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = "mysql", Pooling = false };
        await using var admin = new MySqlConnection(root.ConnectionString);
        await admin.OpenAsync();
        var marker = "fullnet_a06_cancel_" + Guid.CreateVersion7().ToString("N");
        using var process = AiRecoveryBackupTransport.StartMySql("mysql", "mysql", root);
        try
        {
            Console.WriteLine($"A06_MYSQL_CLIENT_PID={process.Id}");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            // mysql 非交互模式默认移除注释，标识必须出现在实际 SQL 字面量中。
            await process.StandardInput.WriteLineAsync("SELECT SLEEP(15), '" + marker + "';");
            await process.StandardInput.FlushAsync();
            // mysql 的非交互输入可能等待 EOF；不依赖客户端的行缓冲行为。
            process.StandardInput.Close();
            var active = 0L;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && active == 0)
            {
                if (process.HasExited)
                {
                    var safeError = (await stderr).Replace(root.Password, "[redacted]", StringComparison.Ordinal);
                    Assert.Fail("测试客户端提前退出 " + process.ExitCode + ": " + safeError);
                }
                active = await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE INFO LIKE @Marker AND ID<>CONNECTION_ID()", new { Marker = "%" + marker + "%" });
                if (active == 0) await Task.Delay(50);
            }
            Assert.AreEqual(1L, active, "必须先观察本客户端的独立查询。");
            await AiRecoveryBackupTransport.StopOwnedProcessAsync(process);
            Assert.IsTrue(process.HasExited, "终止必须等待实际子进程退出。");
            deadline = DateTime.UtcNow.AddSeconds(20);
            do
            {
                active = await admin.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE INFO LIKE @Marker AND ID<>CONNECTION_ID()", new { Marker = "%" + marker + "%" });
                if (active > 0) await Task.Delay(100);
            } while (active > 0 && DateTime.UtcNow < deadline);
            Assert.AreEqual(0L, active, "不能在服务端会话未自然排空时清理资源。");
            _ = await stdout;
            _ = await stderr;
        }
        finally { await AiRecoveryBackupTransport.StopOwnedProcessAsync(process); }
    }
}
