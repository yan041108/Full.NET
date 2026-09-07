using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>使用两条真实连接验证生成租约争抢、远程取消、到期接替及旧清理隔离。</summary>
[TestClass]
public sealed class AiGenerationLeasePersistenceTests
{
    /// <summary>两个实例只能一个获权，旧代释放不能影响到期后取得租约的新代。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Lease_competition_cancellation_and_takeover_are_fenced_async(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false)
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
        {
            Provider = provider, ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300,
        }), NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync().ConfigureAwait(false);
        await using var first = Connection(provider, connectionString);
        await using var second = Connection(provider, connectionString);
        await first.OpenAsync().ConfigureAwait(false);
        await second.OpenAsync().ConfigureAwait(false);
        var session = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var tenant = Guid.CreateVersion7();
        var generationA = Guid.CreateVersion7();
        var generationB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await first.ExecuteAsync(
            """
            INSERT INTO fn_ai_chat_session
                (Id, TenantId, OwnerUserId, ModelConfigId, ModelName, Title, MessageCount, IsGenerating, CreatedAtUtc, Version)
            VALUES (@Id, @TenantId, @OwnerUserId, @ModelConfigId, 'test', 'test', 0, 0, @Now, 1)
            """, new { Id = session, TenantId = tenant, OwnerUserId = owner, ModelConfigId = Guid.CreateVersion7(), Now = now }).ConfigureAwait(false);
        object Parameters(Guid generation, DateTime time, Guid? scopeTenant = null) => new
        {
            SessionId = session, OwnerUserId = owner, ScopeTenantId = scopeTenant ?? tenant,
            GenerationId = generation, Now = time, ExpiresAtUtc = time.AddSeconds(30),
        };
        var claims = await Task.WhenAll(
            first.ExecuteAsync(Sql("Acquire"), Parameters(generationA, now)),
            second.ExecuteAsync(Sql("Acquire"), Parameters(generationB, now))).ConfigureAwait(false);
        Assert.AreEqual(1, claims.Sum());
        var winner = claims[0] == 1 ? generationA : generationB;
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("RequestCancellation"), Parameters(winner, now, Guid.CreateVersion7())).ConfigureAwait(false));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("RequestCancellation"), Parameters(winner, now)).ConfigureAwait(false));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Renew"), Parameters(winner, now.AddSeconds(1))).ConfigureAwait(false));
        var next = Guid.CreateVersion7();
        var later = now.AddSeconds(31);
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Acquire"), Parameters(next, later)).ConfigureAwait(false));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Release"), Parameters(winner, later)).ConfigureAwait(false));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Renew"), Parameters(next, later.AddSeconds(1))).ConfigureAwait(false));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Release"), Parameters(next, later.AddSeconds(2))).ConfigureAwait(false));
        Assert.AreEqual(0, await first.ExecuteScalarAsync<int>("SELECT IsGenerating FROM fn_ai_chat_session WHERE Id=@Id", new { Id = session }).ConfigureAwait(false));
    }

    /// <summary>读取实际生产 SQL，避免复制一份实现使集成测试失去约束作用。</summary>
    /// <param name="field">租约语句声明名。</param>
    private static string Sql(string field) => ((SqlStatement)typeof(AiModule).Assembly
        .GetType("Full.NET.Modules.Ai.Persistence.AiChatGenerationSql", true)!
        .GetField(field)!.GetValue(null)!).Text;

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">隔离数据库连接字符串。</param>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
