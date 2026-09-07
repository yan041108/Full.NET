using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>使用生产挑战 SQL 验证失败计数原子递增、跨作用域拒绝以及一次性消费。</summary>
internal static class NotificationChallengePersistenceAssertions
{
    /// <summary>两条连接同时递增且上限为 1 时，只能有一次成功；错误 TenantScopeKey 不得改写计数。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Concurrent_increment_stops_at_max_attempts_async(DatabaseProvider provider)
    {
        var (first, second, challengeId, tenantScope, userId) = await SeedChallengeAsync(provider, maxAttempts: 1).ConfigureAwait(false);
        await using var firstConnection = first;
        await using var secondConnection = second;
        var claims = await Task.WhenAll(
            firstConnection.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.IncrementAttempt.Text,
                new { Id = challengeId, TenantScopeKey = tenantScope, UserId = userId }),
            secondConnection.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.IncrementAttempt.Text,
                new { Id = challengeId, TenantScopeKey = tenantScope, UserId = userId })).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Sum());
        Assert.AreEqual(0, await firstConnection.ExecuteAsync(
            NotificationRecipientEndpointChallengeSql.IncrementAttempt.Text,
            new { Id = challengeId, TenantScopeKey = "other-scope", UserId = userId }).ConfigureAwait(false));
        Assert.AreEqual(1, await firstConnection.QuerySingleAsync<int>(
            "SELECT AttemptCount FROM fn_notifications_recipient_endpoint_challenge WHERE Id=@Id",
            new { Id = challengeId }).ConfigureAwait(false));
    }

    /// <summary>挑战只能被匹配作用域消费一次；第二次与跨作用域消费都必须失败。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Consume_is_one_shot_and_scope_bound_async(DatabaseProvider provider)
    {
        var (first, second, challengeId, tenantScope, userId) = await SeedChallengeAsync(provider, maxAttempts: 5).ConfigureAwait(false);
        await using var connection = first;
        await using var unused = second;
        var now = DateTime.UtcNow;
        Assert.AreEqual(0, await connection.ExecuteAsync(
            NotificationRecipientEndpointChallengeSql.MarkConsumed.Text,
            new { Id = challengeId, TenantScopeKey = "other-scope", UserId = userId, ConsumedAtUtc = now }).ConfigureAwait(false));
        var consumed = await Task.WhenAll(
            connection.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.MarkConsumed.Text,
                new { Id = challengeId, TenantScopeKey = tenantScope, UserId = userId, ConsumedAtUtc = now }),
            unused.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.MarkConsumed.Text,
                new { Id = challengeId, TenantScopeKey = tenantScope, UserId = userId, ConsumedAtUtc = now.AddSeconds(1) })).ConfigureAwait(false);
        Assert.AreEqual(1, consumed.Sum());
        Assert.AreEqual(0, await connection.ExecuteAsync(
            NotificationRecipientEndpointChallengeSql.MarkConsumed.Text,
            new { Id = challengeId, TenantScopeKey = tenantScope, UserId = userId, ConsumedAtUtc = now.AddSeconds(2) }).ConfigureAwait(false));
    }

    /// <summary>关闭挑战外键后写入一行未消费挑战。</summary>
    private static async Task<(DbConnection First, DbConnection Second, Guid ChallengeId, string TenantScopeKey, Guid UserId)> SeedChallengeAsync(
        DatabaseProvider provider,
        int maxAttempts)
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
        await DisableChallengeForeignKeyAsync(provider, first).ConfigureAwait(false);
        var challengeId = Guid.CreateVersion7();
        var tenantScope = $"tenant:{Guid.CreateVersion7():N}";
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await first.ExecuteAsync(
            NotificationRecipientEndpointChallengeSql.Insert.Text,
            new
            {
                Id = challengeId,
                RecipientEndpointId = Guid.CreateVersion7(),
                TenantScopeKey = tenantScope,
                UserId = userId,
                CodeHash = new string('b', 64),
                MaxAttempts = maxAttempts,
                ExpiresAtUtc = now.AddMinutes(10),
                CreatedAtUtc = now,
            }).ConfigureAwait(false);
        return (first, second, challengeId, tenantScope, userId);
    }

    /// <summary>隔离库上关闭挑战外键，只验证计数与消费 SQL。</summary>
    private static Task DisableChallengeForeignKeyAsync(DatabaseProvider provider, DbConnection connection) =>
        provider == DatabaseProvider.MySql
            ? connection.ExecuteAsync("SET FOREIGN_KEY_CHECKS = 0")
            : connection.ExecuteAsync(
                "ALTER TABLE dbo.fn_notifications_recipient_endpoint_challenge NOCHECK CONSTRAINT FK_fn_notifications_endpoint_challenge_Endpoint");

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
