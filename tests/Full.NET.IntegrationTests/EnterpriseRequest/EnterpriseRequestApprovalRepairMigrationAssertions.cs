using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

internal static partial class EnterpriseRequestAssertions
{
    // 在同一个审批夹具中模拟 249 DDL 部分完成但 DbUp 未记账，保留已验收的恢复记录。
    private static async Task VerifyApprovalRepairMigrationReentryAsync(FullNetApiFactory factory, DbConnection connection, Guid requestId)
    {
        const string index = "UX_demo_enterprise_request_approval_repair_Request_Version_Kind";
        await connection.ExecuteAsync($"DROP INDEX {index} ON demo_enterprise_request_approval_repair");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "249_DemoEnterpriseRequestApprovalRepair.sql", factory.Provider == DatabaseProvider.SqlServer);
        var directory = Directory.CreateTempSubdirectory("fullnet-repair249-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "framework-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new {
                migrationInventory = new { selectionStatus = "preset-recovery-249", scripts = new[] { new { name = "249_DemoEnterpriseRequestApprovalRepair.sql" } } } }));
            var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions { Provider = factory.Provider,
                ConnectionString = factory.ConnectionString, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
                NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions(),
                Options.Create(new FrameworkManifestMigrationOptions { ContentRoot = directory }));
            Assert.AreEqual(1, (await runner.MigrateAsync()).ExecutedScriptCount);
            Assert.AreEqual(0, (await runner.MigrateAsync()).ExecutedScriptCount);
            Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id", new { Id = requestId }));
            await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync("""
                INSERT INTO demo_enterprise_request_approval_repair
                  (Id, TenantId, RequestId, WorkflowInstanceId, RequestVersion, KindKey, WorkflowStatusKey, ActorUserId, Reason, CreatedAtUtc)
                SELECT @NewId, TenantId, RequestId, WorkflowInstanceId, RequestVersion, KindKey, WorkflowStatusKey, ActorUserId, Reason, CreatedAtUtc
                FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id AND KindKey = 'reconcile'
                """, new { NewId = Guid.CreateVersion7(), Id = requestId }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
