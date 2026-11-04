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
    // 在同一个审批夹具中模拟应用 006 DDL 部分完成但 DbUp 未记账，保留已验收的恢复记录。
    private static async Task VerifyApprovalRepairMigrationReentryAsync(FullNetApiFactory factory, DbConnection connection, Guid requestId)
    {
        const string index = "UX_demo_enterprise_request_approval_repair_Request_Version_Kind";
        await connection.ExecuteAsync($"DROP INDEX {index} ON demo_enterprise_request_approval_repair");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "006_EnterpriseRequestApprovalRepair.sql", factory.Provider == DatabaseProvider.SqlServer);
        {
            Assert.AreEqual(1, SampleMigrationRecoverySupport.Run(factory.Provider, factory.ConnectionString, "006_EnterpriseRequestApprovalRepair.sql"));
            Assert.AreEqual(0, SampleMigrationRecoverySupport.Run(factory.Provider, factory.ConnectionString, "006_EnterpriseRequestApprovalRepair.sql"));
            Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id", new { Id = requestId }));
            await Assert.ThrowsAsync<DbException>(async () => await connection.ExecuteAsync("""
                INSERT INTO demo_enterprise_request_approval_repair
                  (Id, TenantId, RequestId, WorkflowInstanceId, RequestVersion, KindKey, WorkflowStatusKey, ActorUserId, Reason, CreatedAtUtc)
                SELECT @NewId, TenantId, RequestId, WorkflowInstanceId, RequestVersion, KindKey, WorkflowStatusKey, ActorUserId, Reason, CreatedAtUtc
                FROM demo_enterprise_request_approval_repair WHERE RequestId = @Id AND KindKey = 'reconcile'
                """, new { NewId = Guid.CreateVersion7(), Id = requestId }));
        }
    }
}
