using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>使用生产配额 SQL 在隔离库上验证并发预留、跨月重置与未知计量不退款。</summary>
internal static class AiQuotaReservationPersistenceAssertions
{
    /// <summary>两条连接同时预留超过剩余额度的 Token 时，只能有一条 UPDATE 成功。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Concurrent_reserve_on_last_tokens_admits_only_one_async(DatabaseProvider provider)
    {
        var (first, second, tenant) = await SeedQuotaAsync(provider, month: "2026-09", usedTokens: 0, usedRequests: 0, tokenLimit: 10000, requestLimit: 10).ConfigureAwait(false);
        await using var firstConnection = first;
        await using var secondConnection = second;
        var now = DateTime.UtcNow;
        var claims = await Task.WhenAll(
            firstConnection.ExecuteAsync(Sql("Reserve"), ReserveParameters(tenant, "2026-09", 6000, now)),
            secondConnection.ExecuteAsync(Sql("Reserve"), ReserveParameters(tenant, "2026-09", 6000, now))).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Sum());
        var used = await firstConnection.QuerySingleAsync<(long Tokens, long Requests, string Month)>(
            "SELECT UsedTokensThisMonth AS Tokens, UsedRequestsThisMonth AS Requests, QuotaMonthKey AS Month FROM fn_ai_tenant_quota WHERE TenantId=@TenantId",
            new { TenantId = tenant }).ConfigureAwait(false);
        Assert.AreEqual(6000, used.Tokens);
        Assert.AreEqual(1, used.Requests);
        Assert.AreEqual("2026-09", used.Month);
    }

    /// <summary>月份向前推进时，本月计数从零开始，不得把上月已用额度带到新月份。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Cross_month_reserve_resets_counters_async(DatabaseProvider provider)
    {
        var (connection, second, tenant) = await SeedQuotaAsync(provider, month: "2026-08", usedTokens: 9000, usedRequests: 3, tokenLimit: 10000, requestLimit: 10).ConfigureAwait(false);
        await using var firstConnection = connection;
        await using var unused = second;
        var now = DateTime.UtcNow;
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(Sql("Reserve"), ReserveParameters(tenant, "2026-09", 6000, now)).ConfigureAwait(false));
        var used = await firstConnection.QuerySingleAsync<(long Tokens, long Requests, string Month)>(
            "SELECT UsedTokensThisMonth AS Tokens, UsedRequestsThisMonth AS Requests, QuotaMonthKey AS Month FROM fn_ai_tenant_quota WHERE TenantId=@TenantId",
            new { TenantId = tenant }).ConfigureAwait(false);
        Assert.AreEqual(6000, used.Tokens);
        Assert.AreEqual(1, used.Requests);
        Assert.AreEqual("2026-09", used.Month);
    }

    /// <summary>未知实际用量仍领取一次性结算权，但 Token 差额为 0，不得把预留退回配额。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Unknown_settle_does_not_refund_reserved_tokens_async(DatabaseProvider provider)
    {
        var (connection, second, tenant) = await SeedQuotaAsync(provider, month: "2026-09", usedTokens: 0, usedRequests: 0, tokenLimit: 10000, requestLimit: 10).ConfigureAwait(false);
        await using var firstConnection = connection;
        await using var unused = second;
        var now = DateTime.UtcNow;
        var reservationId = Guid.CreateVersion7();
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(Sql("Reserve"), ReserveParameters(tenant, "2026-09", 1000, now)).ConfigureAwait(false));
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(Sql("Insert"), new
        {
            Id = reservationId,
            TenantId = tenant,
            QuotaMonthKey = "2026-09",
            ReservedTokens = 1000L,
            UpdatedAtUtc = now,
        }).ConfigureAwait(false));
        var settle = new
        {
            Id = reservationId,
            TenantId = tenant,
            QuotaMonthKey = "2026-09",
            ReservedTokens = 1000L,
            ActualTokens = (long?)null,
            TokenDelta = 0L,
            UpdatedAtUtc = now.AddSeconds(1),
        };
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(Sql("Settle"), settle).ConfigureAwait(false));
        Assert.AreEqual(1, await firstConnection.ExecuteAsync(Sql("Adjust"), settle).ConfigureAwait(false));
        Assert.AreEqual(0, await firstConnection.ExecuteAsync(Sql("Settle"), settle).ConfigureAwait(false));
        var used = await firstConnection.QuerySingleAsync<long>(
            "SELECT UsedTokensThisMonth FROM fn_ai_tenant_quota WHERE TenantId=@TenantId",
            new { TenantId = tenant }).ConfigureAwait(false);
        Assert.AreEqual(1000, used);
        var settled = await firstConnection.QuerySingleAsync<(int IsSettled, long? Actual)>(
            "SELECT CASE WHEN IsSettled = 1 THEN 1 ELSE 0 END AS IsSettled, ActualTokens AS Actual FROM fn_ai_quota_reservation WHERE Id=@Id",
            new { Id = reservationId }).ConfigureAwait(false);
        Assert.AreEqual(1, settled.IsSettled);
        Assert.IsNull(settled.Actual);
    }

    /// <summary>迁移隔离库并写入一条启用中的租户配额，返回两条独立连接。</summary>
    private static async Task<(DbConnection First, DbConnection Second, Guid TenantId)> SeedQuotaAsync(
        DatabaseProvider provider,
        string month,
        long usedTokens,
        long usedRequests,
        long tokenLimit,
        long requestLimit)
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
        var tenant = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await first.ExecuteAsync(
            """
            INSERT INTO fn_ai_tenant_quota
                (Id, TenantId, MonthlyTokenLimit, MonthlyRequestLimit, UsedTokensThisMonth, UsedRequestsThisMonth,
                 QuotaMonthKey, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES
                (@Id, @TenantId, @MonthlyTokenLimit, @MonthlyRequestLimit, @UsedTokensThisMonth, @UsedRequestsThisMonth,
                 @QuotaMonthKey, 1, @Now, @Now, 1)
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                MonthlyTokenLimit = tokenLimit,
                MonthlyRequestLimit = requestLimit,
                UsedTokensThisMonth = usedTokens,
                UsedRequestsThisMonth = usedRequests,
                QuotaMonthKey = month,
                Now = now,
            }).ConfigureAwait(false);
        return (first, second, tenant);
    }

    /// <summary>构造与生产 Guard 一致的预留参数。</summary>
    private static object ReserveParameters(Guid tenantId, string month, long tokens, DateTime now) => new
    {
        TenantId = tenantId,
        QuotaMonthKey = month,
        ReservedTokens = tokens,
        UpdatedAtUtc = now,
    };

    /// <summary>读取实际生产 SQL，避免复制一份实现使集成测试失去约束作用。</summary>
    /// <param name="field">配额语句声明名。</param>
    private static string Sql(string field) => ((SqlStatement)typeof(AiModule).Assembly
        .GetType("Full.NET.Modules.Ai.Persistence.AiQuotaReservationSql", true)!
        .GetField(field)!.GetValue(null)!).Text;

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">隔离数据库连接字符串。</param>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
