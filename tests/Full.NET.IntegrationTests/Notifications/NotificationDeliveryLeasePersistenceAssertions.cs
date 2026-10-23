using System.Data.Common;
using Dapper;
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>使用生产投递领取 SQL 验证双 Worker 争抢、过期重领以及过期后旧代次不能完成或续租。</summary>
internal static class NotificationDeliveryLeasePersistenceAssertions
{
    /// <summary>两条连接同时领取同一 accepted 投递时，只能有一个写入租约世代。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Concurrent_claim_admits_only_one_owner_async(DatabaseProvider provider)
    {
        var (first, second, deliveryId) = await SeedAcceptedDeliveryAsync(provider).ConfigureAwait(false);
        await using var firstConnection = first;
        await using var secondConnection = second;
        var now = DateTime.UtcNow;
        var claims = await Task.WhenAll(
            ClaimAsync(provider, firstConnection, "worker-a", now),
            ClaimAsync(provider, secondConnection, "worker-b", now)).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Count(row => row is not null && row.Id == deliveryId));
        Assert.AreEqual(1, claims.Count(row => row is null));
        var winner = claims.First(row => row is not null)!;
        Assert.AreEqual(2, winner.LeaseGeneration);
        Assert.AreEqual(2, winner.Revision);
        Assert.AreEqual(1L, await firstConnection.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
    }

    /// <summary>租约到期后另一 Worker 可重领；旧世代的续租和完成必须失败。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Expired_lease_can_be_reclaimed_and_old_generation_is_fenced_async(DatabaseProvider provider)
    {
        var (first, second, deliveryId) = await SeedAcceptedDeliveryAsync(provider).ConfigureAwait(false);
        await using var firstConnection = first;
        await using var secondConnection = second;
        var now = DateTime.UtcNow;
        var firstClaim = await ClaimAsync(provider, firstConnection, "worker-a", now).ConfigureAwait(false);
        Assert.IsNotNull(firstClaim);
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(
            NotificationPlatformSql.ExpireDeliveryLease.Text,
            new { Id = deliveryId, ExpiredAt = now.AddSeconds(-1) }).ConfigureAwait(false));
        var later = now.AddSeconds(2);
        var secondClaim = await ClaimAsync(provider, secondConnection, "worker-b", later).ConfigureAwait(false);
        Assert.IsNotNull(secondClaim);
        Assert.AreEqual("worker-b", secondClaim.LeaseOwnerKey);
        Assert.AreEqual(3, secondClaim.LeaseGeneration);
        // 未完成的领取保留预算，租约栅栏与预算分别持久化。
        Assert.AreEqual(2L, await firstConnection.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
        Assert.AreEqual(0, await firstConnection.ExecuteAsync(
            NotificationPlatformSql.RenewDeliveryLease.Text,
            new
            {
                Id = deliveryId,
                LeaseOwnerKey = firstClaim.LeaseOwnerKey,
                LeaseGeneration = firstClaim.LeaseGeneration,
                Revision = firstClaim.Revision,
                LeaseExpiresAtUtc = later.AddSeconds(60),
                Now = later,
            }).ConfigureAwait(false));
        Assert.AreEqual(0, await firstConnection.ExecuteAsync(
            NotificationPlatformSql.CompleteDelivery.Text,
            new
            {
                Id = deliveryId,
                StatusKey = "sent",
                NextAttemptAtUtc = (DateTime?)null,
                SuppressionReasonCode = (string?)null,
                LeaseGeneration = firstClaim.LeaseGeneration,
                Revision = firstClaim.Revision,
                LeaseOwnerKey = firstClaim.LeaseOwnerKey,
                Now = later,
            }).ConfigureAwait(false));
        Assert.AreEqual(1, await secondConnection.ExecuteAsync(
            NotificationPlatformSql.CompleteDelivery.Text,
            new
            {
                Id = deliveryId,
                StatusKey = "sent",
                NextAttemptAtUtc = (DateTime?)null,
                SuppressionReasonCode = (string?)null,
                LeaseGeneration = secondClaim.LeaseGeneration,
                Revision = secondClaim.Revision,
                LeaseOwnerKey = secondClaim.LeaseOwnerKey,
                Now = later,
            }).ConfigureAwait(false));
        Assert.AreEqual("sent", await firstConnection.QuerySingleAsync<string>(
            "SELECT StatusKey FROM fn_notifications_delivery WHERE Id=@Id",
            new { Id = deliveryId }).ConfigureAwait(false));
    }

    /// <summary>有效租约持有者可以续租；世代或修订不匹配时不得延长。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Renew_requires_current_generation_and_revision_async(DatabaseProvider provider)
    {
        var (first, second, deliveryId) = await SeedAcceptedDeliveryAsync(provider).ConfigureAwait(false);
        await using var connection = first;
        await using var unused = second;
        var now = DateTime.UtcNow;
        var claimed = await ClaimAsync(provider, connection, "worker-a", now).ConfigureAwait(false);
        Assert.IsNotNull(claimed);
        Assert.AreEqual(0, await connection.ExecuteAsync(
            NotificationPlatformSql.RenewDeliveryLease.Text,
            new
            {
                Id = deliveryId,
                LeaseOwnerKey = claimed.LeaseOwnerKey,
                LeaseGeneration = claimed.LeaseGeneration,
                Revision = claimed.Revision + 1,
                LeaseExpiresAtUtc = now.AddSeconds(90),
                Now = now.AddSeconds(1),
            }).ConfigureAwait(false));
        Assert.AreEqual(1, await connection.ExecuteAsync(
            NotificationPlatformSql.RenewDeliveryLease.Text,
            new
            {
                Id = deliveryId,
                LeaseOwnerKey = claimed.LeaseOwnerKey,
                LeaseGeneration = claimed.LeaseGeneration,
                Revision = claimed.Revision,
                LeaseExpiresAtUtc = now.AddSeconds(90),
                Now = now.AddSeconds(1),
            }).ConfigureAwait(false));

        // 多轮治理延迟只退还预算，世代必须持续递增；随后人工重试也不能复用旧配额世代。
        for (var index = 0; index < 5; index++)
        {
            Assert.AreEqual(1, await CompleteAsync("accepted").ConfigureAwait(false));
            Assert.AreEqual(1, await connection.ExecuteAsync(
                NotificationPlatformSql.RefundGovernanceAttemptBudget.Text,
                new { Id = deliveryId, claimed.LeaseGeneration, Revision = claimed.Revision + 1 }).ConfigureAwait(false));
            Assert.AreEqual(0L, await connection.QuerySingleAsync<long>(
                NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
            var oldGeneration = claimed.LeaseGeneration;
            // 下一轮模拟时钟前进，避免同一墙上时刻在两库不同小数精度下误判为尚未到期。
            now = now.AddSeconds(1);
            claimed = await ClaimAsync(provider, connection, "worker-a", now).ConfigureAwait(false);
            Assert.IsNotNull(claimed);
            Assert.IsGreaterThan(oldGeneration, claimed.LeaseGeneration);
            Assert.AreEqual(1L, await connection.QuerySingleAsync<long>(
                NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
        }

        Assert.AreEqual(1, await CompleteAsync("failed").ConfigureAwait(false));
        Assert.AreEqual(1, await connection.ExecuteAsync(NotificationPlatformSql.RetryDelivery.Text,
            new { Id = deliveryId, NextAttemptAtUtc = now, Now = now, Revision = claimed.Revision + 1 }).ConfigureAwait(false));
        Assert.AreEqual(0L, await connection.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
        var previousGeneration = claimed.LeaseGeneration;
        now = now.AddSeconds(1);
        claimed = await ClaimAsync(provider, connection, "worker-a", now).ConfigureAwait(false);
        Assert.IsNotNull(claimed);
        Assert.IsGreaterThan(previousGeneration, claimed.LeaseGeneration);
        Assert.AreEqual(1L, await connection.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));

        Task<int> CompleteAsync(string status) => connection.ExecuteAsync(
            NotificationPlatformSql.CompleteDelivery.Text,
            new
            {
                Id = deliveryId,
                StatusKey = status,
                NextAttemptAtUtc = now,
                SuppressionReasonCode = (string?)null,
                claimed.LeaseGeneration,
                claimed.Revision,
                claimed.LeaseOwnerKey,
                Now = now,
            });
    }

    /// <summary>关闭投递表外键后写入一条 accepted 行，只验证租约 SQL 本身。</summary>
    private static async Task<(DbConnection First, DbConnection Second, Guid DeliveryId)> SeedAcceptedDeliveryAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false)
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }), NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync().ConfigureAwait(false);
        var first = Connection(provider, connectionString);
        var second = Connection(provider, connectionString);
        await first.OpenAsync().ConfigureAwait(false);
        await second.OpenAsync().ConfigureAwait(false);
        await DisableDeliveryForeignKeysAsync(provider, first).ConfigureAwait(false);
        var deliveryId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await first.ExecuteAsync(
            NotificationPlatformSql.InsertDelivery.Text,
            new
            {
                Id = deliveryId,
                IntentId = Guid.CreateVersion7(),
                RecipientId = Guid.CreateVersion7(),
                ChannelKey = "email",
                ProviderProfileVersionId = (Guid?)null,
                BindingVersionId = (Guid?)null,
                StatusKey = "accepted",
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
                ContentSnapshotJson = (string?)null,
                PreferenceVersion = (long?)null,
                PolicyDecisionKey = (string?)null,
                SuppressionReasonCode = (string?)null,
            }).ConfigureAwait(false);

        // 模拟 DDL 已完成、回填尚未完成且 DbUp 未记账；恢复后保守计入历史领取预算。
        await first.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? """
              ALTER TABLE fn_notifications_delivery DROP CONSTRAINT DF_fn_notifications_delivery_AttemptBudgetUsed;
              ALTER TABLE fn_notifications_delivery DROP COLUMN AttemptBudgetUsed;
              ALTER TABLE fn_notifications_delivery ADD AttemptBudgetUsed bigint NULL;
              """
            : """
              ALTER TABLE fn_notifications_delivery DROP COLUMN AttemptBudgetUsed;
              ALTER TABLE fn_notifications_delivery ADD AttemptBudgetUsed bigint NULL;
              """).ConfigureAwait(false);
        await first.ExecuteAsync(
            "UPDATE fn_notifications_delivery SET LeaseGeneration = 4 WHERE Id = @Id",
            new { Id = deliveryId }).ConfigureAwait(false);
        await ForgetBudgetMigrationAsync().ConfigureAwait(false);
        Assert.AreEqual(1, RecoverBudgetMigration());
        Assert.AreEqual(3L, await first.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
        Assert.AreEqual(0, RecoverBudgetMigration());

        // 模拟结构收紧已完成但仍未记账；重跑不得覆盖已持久化的真实预算。
        await first.ExecuteAsync("UPDATE fn_notifications_delivery SET AttemptBudgetUsed = 2 WHERE Id = @Id",
            new { Id = deliveryId }).ConfigureAwait(false);
        await ForgetBudgetMigrationAsync().ConfigureAwait(false);
        Assert.AreEqual(1, RecoverBudgetMigration());
        Assert.AreEqual(2L, await first.QuerySingleAsync<long>(
            NotificationPlatformSql.FindAttemptBudget.Text, new { Id = deliveryId }).ConfigureAwait(false));
        await first.ExecuteAsync(
            "UPDATE fn_notifications_delivery SET LeaseGeneration = 1, AttemptBudgetUsed = 0 WHERE Id = @Id",
            new { Id = deliveryId }).ConfigureAwait(false);
        return (first, second, deliveryId);

        // 恢复只选择目标脚本，未来迁移不会跑穿这里模拟的半完成结构。
        int RecoverBudgetMigration()
        {
            var builder = provider == DatabaseProvider.SqlServer
                ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(
                    connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var segment = provider == DatabaseProvider.SqlServer ? ".SqlServer." : ".MySql.";
            var result = builder.WithScriptsEmbeddedInAssembly(MigrationAssembly.Value,
                    name => name.Contains(segment, StringComparison.Ordinal)
                        && name.EndsWith(".282_NotificationsAttemptBudget.sql", StringComparison.Ordinal))
                .WithExecutionTimeout(TimeSpan.FromSeconds(300))
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            return result.Scripts.Count();
        }

        Task<int> ForgetBudgetMigrationAsync() => first.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%282_NotificationsAttemptBudget.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%282_NotificationsAttemptBudget.sql'");
    }

    /// <summary>按提供程序执行生产领取语句。</summary>
    private static async Task<ClaimedDelivery?> ClaimAsync(
        DatabaseProvider provider,
        DbConnection connection,
        string leaseOwnerKey,
        DateTime now)
    {
        var leaseExpiresAtUtc = now.AddSeconds(30);
        if (provider == DatabaseProvider.SqlServer)
        {
            var rows = (await connection.QueryAsync<ClaimedDelivery>(
                NotificationPlatformSql.ClaimDeliveriesSqlServer.Text,
                new { BatchSize = 1, Now = now, LeaseOwnerKey = leaseOwnerKey, LeaseExpiresAtUtc = leaseExpiresAtUtc }).ConfigureAwait(false)).AsList();
            return rows.Count == 0 ? null : rows[0];
        }

        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var ids = (await connection.QueryAsync<Guid>(
            NotificationPlatformSql.SelectClaimableDeliveryIdsMySql.Text,
            new { BatchSize = 1, Now = now },
            transaction).ConfigureAwait(false)).AsList();
        if (ids.Count == 0)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return null;
        }

        await connection.ExecuteAsync(
            NotificationPlatformSql.ClaimDeliveriesByIdsMySql.Text,
            new { Ids = ids, LeaseOwnerKey = leaseOwnerKey, LeaseExpiresAtUtc = leaseExpiresAtUtc, Now = now },
            transaction).ConfigureAwait(false);
        var claimed = (await connection.QueryAsync<ClaimedDelivery>(
            NotificationPlatformSql.SelectDeliveriesByLease.Text,
            new { LeaseOwnerKey = leaseOwnerKey },
            transaction).ConfigureAwait(false)).AsList();
        await transaction.CommitAsync().ConfigureAwait(false);
        return claimed.Count == 0 ? null : claimed[0];
    }

    /// <summary>隔离库上关闭投递外键，避免为锁测试装配完整模板图。</summary>
    private static Task DisableDeliveryForeignKeysAsync(DatabaseProvider provider, DbConnection connection) =>
        provider == DatabaseProvider.MySql
            ? connection.ExecuteAsync("SET FOREIGN_KEY_CHECKS = 0")
            : connection.ExecuteAsync(
                """
                ALTER TABLE dbo.fn_notifications_delivery NOCHECK CONSTRAINT FK_fn_notifications_delivery_Intent;
                ALTER TABLE dbo.fn_notifications_delivery NOCHECK CONSTRAINT FK_fn_notifications_delivery_Recipient;
                ALTER TABLE dbo.fn_notifications_delivery NOCHECK CONSTRAINT FK_fn_notifications_delivery_ProfileVersion;
                ALTER TABLE dbo.fn_notifications_delivery NOCHECK CONSTRAINT FK_fn_notifications_delivery_BindingVersion;
                """);

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

    /// <summary>领取结果只保留 fencing 所需列。</summary>
    internal sealed class ClaimedDelivery
    {
        public Guid Id { get; set; }
        public string? LeaseOwnerKey { get; set; }
        public long LeaseGeneration { get; set; }
        public long Revision { get; set; }
    }
}
