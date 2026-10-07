using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>真实双库验证同版本并发授权幂等、不同租户独立与迁移重放保留授权数据。</summary>
[TestClass]
public sealed class Migration245ReportingTenantGrantTests
{
    [TestMethod]
    public Task SqlServer_grant_idempotency_and_reentry() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_grant_idempotency_and_reentry() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var migration = new Through245Scope(provider, cs); await migration.Runner.MigrateAsync();
        var tenant = Guid.CreateVersion7(); var definition = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        await Task.WhenAll(GrantAsync(tenant), GrantAsync(tenant));
        await GrantAsync(Guid.CreateVersion7());
        await using var connection = Connection(); await connection.OpenAsync();
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_reporting_definition_tenant_grant"));
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("DROP INDEX UX_fn_reporting_grant_Tenant_Definition_Version ON dbo.fn_reporting_definition_tenant_grant");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "245_ReportingTenantGrant.sql", provider == DatabaseProvider.SqlServer);
        Assert.AreEqual(1, (await migration.Runner.MigrateAsync()).ExecutedScriptCount);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_reporting_definition_tenant_grant"));
        await GrantAsync(tenant);
        Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_reporting_definition_tenant_grant"));

        DbConnection Connection() => provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        async Task GrantAsync(Guid targetTenant)
        {
            await using var grantConnection = Connection(); await grantConnection.OpenAsync();
            await grantConnection.ExecuteAsync(ReportingTenantGrantSql.Grant(provider).Text,
                new { Id = Guid.CreateVersion7(), TargetTenantId = targetTenant, DefinitionId = definition,
                    VersionNumber = 1, GrantedByUserId = actor, CreatedAtUtc = DateTime.UtcNow });
        }
    }

    // 清单冻结目标脚本，后续迁移不能漂入本恢复测试。
    private sealed class Through245Scope : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("fullnet-grant245-").FullName;
        internal Full.NET.Migrations.DbUp.DbUpMigrationRunner Runner { get; }
        internal Through245Scope(DatabaseProvider provider, string connectionString)
        {
            var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
            var names = typeof(Full.NET.Migrations.DbUp.DbUpMigrationRunner).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(segment, StringComparison.Ordinal)
                    && NamingExpandTestMigrationRunner.IsThroughMigration(name, 245)).ToArray();
            Assert.IsTrue(names.Any(name => name.EndsWith("245_ReportingTenantGrant.sql", StringComparison.Ordinal)));
            var scripts = names.Select(name => new { name = name[(name.IndexOf(segment, StringComparison.Ordinal) + segment.Length)..] });
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new
                { migrationInventory = new { selectionStatus = "preset-recovery-through245", scripts } }));
            Runner = new(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
                { Provider = provider, ConnectionString = connectionString, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Microsoft.Extensions.Options.Options.Create(new Full.NET.Migrations.DbUp.FrameworkManifestMigrationOptions { ContentRoot = directory }));
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

}
