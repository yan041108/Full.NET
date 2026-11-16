using DotNet.Testcontainers.Containers;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>通过测试容器的原生备份工具将通知数据复制到新登记的恢复库。</summary>
internal static class NotificationRecoveryBackup
{
    internal static Task<string> RestoreAsync(DatabaseProvider provider, string sourceConnection,
        Func<Task> afterBackup, CancellationToken token) => provider == DatabaseProvider.SqlServer
        ? RestoreSqlServerAsync(sourceConnection, afterBackup, token)
        : RestoreMySqlAsync(sourceConnection, afterBackup, token);

    private static async Task<string> RestoreSqlServerAsync(string sourceConnection, Func<Task> afterBackup, CancellationToken token)
    {
        var container = await SharedDatabaseFixture.GetOrStartSqlServerAsync();
        var source = SharedDatabaseFixture.GetSqlServerDatabaseName(sourceConnection);
        var media = $"/var/opt/mssql/data/a06_recovery_{Guid.NewGuid():N}.bak";
        var ownedMedia = new HashSet<string>(StringComparer.Ordinal) { media };
        var ownedDatabases = new HashSet<string>(StringComparer.Ordinal) { source };
        await using var admin = new SqlConnection(await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync());
        await admin.OpenAsync(token);
        try
        {
            await AiRecoveryBackupTransport.BackupSqlServerAsync(admin, source, media, ownedDatabases, ownedMedia, token);
            await afterBackup();
            return await SharedDatabaseFixture.RegisterRestoredSqlServerDatabaseAsync(target =>
            {
                ownedDatabases.Add(target);
                return AiRecoveryBackupTransport.RestoreSqlServerAsync(admin, target, media, ownedDatabases, ownedMedia, token);
            });
        }
        finally { await RemoveMediaAsync(container, media); }
    }

    private static async Task<string> RestoreMySqlAsync(string sourceConnection, Func<Task> afterBackup, CancellationToken token)
    {
        var container = await SharedDatabaseFixture.GetOrStartMySqlAsync();
        var source = SharedDatabaseFixture.GetMySqlDatabaseName(sourceConnection);
        var targetConnection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var target = SharedDatabaseFixture.GetMySqlDatabaseName(targetConnection);
        var media = $"/tmp/fullnet-notification-recovery-{Guid.NewGuid():N}.sql";
        try
        {
            await RunMySqlAsync("""
                set -euC
                MYSQL_PWD="$MYSQL_ROOT_PASSWORD" timeout 90s mysqldump -uroot --single-transaction --no-tablespaces --hex-blob --set-gtid-purged=OFF -- "$1" > "$2"
                test -s "$2"
                """, source);
            await afterBackup();
            await RunMySqlAsync("""
                set -eu
                MYSQL_PWD="$MYSQL_ROOT_PASSWORD" timeout 90s mysql -uroot -- "$1" < "$2"
                """, target);
            return targetConnection;
        }
        finally { await RemoveMediaAsync(container, media); }

        async Task RunMySqlAsync(string command, string database)
        {
            var result = await container.ExecAsync(["sh", "-c", command, "fullnet-notification-recovery", database, media], token);
            Assert.AreEqual(0L, result.ExitCode, "MySQL 原生备份或恢复失败。");
        }
    }

    private static async Task RemoveMediaAsync(IContainer container, string media)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await container.ExecAsync(["rm", "-f", "--", media], deadline.Token);
        Assert.AreEqual(0L, result.ExitCode, "无法清理本次测试备份文件。");
    }
}
