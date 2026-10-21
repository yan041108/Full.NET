using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>真实双库验证性能批次中断恢复、派生预算及跨租户版本投影；复用现有独立数据库夹具。</summary>
[TestClass]
public sealed class Migration273PerformanceBatchRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_performance_schema_and_monotonic_projection() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_performance_schema_and_monotonic_projection() => VerifyAsync(false);
    private static async Task VerifyAsync(bool sqlServer)
    {
        var connection=await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        var provider=sqlServer?DatabaseProvider.SqlServer:DatabaseProvider.MySql;
        using var factory=new FullNetApiFactory(provider,connection); await factory.InitializeAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer,connection);
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_document_preview_task ALTER COLUMN RequestedByUserId uniqueidentifier NOT NULL"
            : "ALTER TABLE fn_document_preview_task MODIFY COLUMN RequestedByUserId binary(16) NOT NULL");
        Replay("276_DocumentAnonymousPreviewRequester.sql"); Replay("276_DocumentAnonymousPreviewRequester.sql");
        Assert.AreEqual(1,await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('fn_document_preview_task') AND name='RequestedByUserId' AND is_nullable=1"
            : "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_document_preview_task' AND COLUMN_NAME='RequestedByUserId' AND IS_NULLABLE='YES'"));
        const string traceIndex="ix_fn_settings_domain_audit_trace_time";
        await db.ExecuteAsync(sqlServer ? $"DROP INDEX {traceIndex} ON fn_settings_domain_audit"
            : $"ALTER TABLE fn_settings_domain_audit DROP INDEX {traceIndex}");
        Replay("273_SettingsDomainAuditTraceLookup.sql"); Replay("273_SettingsDomainAuditTraceLookup.sql");
        Assert.AreEqual(1,await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_settings_domain_audit') AND name=@Name"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_settings_domain_audit' AND INDEX_NAME=@Name", new { Name=traceIndex }));
        var tenantId=Guid.CreateVersion7(); var otherTenant=Guid.CreateVersion7();
        const string eventType="fullnet.workflow.instance.completed";
        foreach (var id in new[] { Guid.CreateVersion7(),Guid.CreateVersion7() })
            await db.ExecuteAsync("""
                INSERT INTO fn_webhooks_subscription (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
                VALUES (@Id,@TenantId,@EventType,'https://93.184.216.34/hooks','test-protected',1,@Now,@Now,1)
                """,new { Id=id,TenantId=tenantId,EventType=eventType,Now=DateTime.UtcNow });
        // 模拟表创建成功而回填未完成，再模拟已有错误派生计数，均不能变成零容量基线。
        Replay("274_WebhookFanoutBudget.sql");
        Assert.AreEqual(2,await BudgetCount());
        await db.ExecuteAsync("UPDATE fn_webhooks_fanout_budget SET UsedCount=19 WHERE TenantId=@TenantId AND EventType=@EventType",new { TenantId=tenantId,EventType=eventType });
        Replay("274_WebhookFanoutBudget.sql"); Assert.AreEqual(2,await BudgetCount());
        await db.ExecuteAsync("DELETE FROM fn_webhooks_fanout_budget WHERE TenantId=@TenantId",new { TenantId=tenantId });
        Replay("274_WebhookFanoutBudget.sql"); Assert.AreEqual(2,await BudgetCount());
        await db.ExecuteAsync("DELETE FROM fn_webhooks_subscription WHERE TenantId=@TenantId",new { TenantId=tenantId });
        Replay("274_WebhookFanoutBudget.sql"); Assert.AreEqual(0,await BudgetCount());
        if (sqlServer) await db.ExecuteAsync("ALTER TABLE fn_organization_user_unit DROP CONSTRAINT DF_fn_organization_user_unit_PublishedProjectionVersion");
        const string projectionIndex = "IX_fn_identity_organization_user_unit_projection_UnitUser";
        await db.ExecuteAsync(sqlServer
            ? $"DROP INDEX {projectionIndex} ON fn_identity_organization_user_unit_projection"
            : $"ALTER TABLE fn_identity_organization_user_unit_projection DROP INDEX {projectionIndex}");
        await db.ExecuteAsync("ALTER TABLE fn_organization_user_unit DROP COLUMN PublishedProjectionVersion");
        Replay("275_IdentityOrganizationUserUnitProjection.sql"); Replay("275_IdentityOrganizationUserUnitProjection.sql");
        Assert.AreEqual(1,await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_identity_organization_user_unit_projection') AND name=@Name"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_identity_organization_user_unit_projection' AND INDEX_NAME=@Name", new { Name=projectionIndex }));
        Assert.AreEqual(1,await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('fn_organization_user_unit') AND name='PublishedProjectionVersion'"
            : "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_organization_user_unit' AND COLUMN_NAME='PublishedProjectionVersion'"));
        await using var scope=factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var serializer=scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
        var handler=new OrganizationUserUnitChangedIntegrationEventHandler(serializer,
            scope.ServiceProvider.GetRequiredService<ICommandExecutor>(),Options.Create(new DatabaseOptions { Provider=provider }));
        var assignmentId=Guid.CreateVersion7(); var userId=Guid.CreateVersion7(); var unitId=Guid.CreateVersion7();
        var latest=new IdentityOrganizationUserUnitChangedIntegrationEvent(tenantId,assignmentId,userId,unitId,false,2,DateTimeOffset.UtcNow);
        await handler.HandleAsync(serializer.Serialize(latest),default);
        await handler.HandleAsync(serializer.Serialize(latest with { IsActive=true,Version=1 }),default);
        await handler.HandleAsync(serializer.Serialize(latest with { TenantId=otherTenant,IsActive=true,Version=1 }),default);
        Replay("275_IdentityOrganizationUserUnitProjection.sql");
        Assert.AreEqual(2,await db.QuerySingleAsync<long>("SELECT SourceVersion FROM fn_identity_organization_user_unit_projection WHERE TenantId=@TenantId AND AssignmentId=@AssignmentId",new { TenantId=tenantId,AssignmentId=assignmentId }));
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT IsActive FROM fn_identity_organization_user_unit_projection WHERE TenantId=@TenantId AND AssignmentId=@AssignmentId",new { TenantId=tenantId,AssignmentId=assignmentId }));
        Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT IsActive FROM fn_identity_organization_user_unit_projection WHERE TenantId=@TenantId AND AssignmentId=@AssignmentId",new { TenantId=otherTenant,AssignmentId=assignmentId }));
        Task<int> BudgetCount() => db.QuerySingleAsync<int>("SELECT UsedCount FROM fn_webhooks_fanout_budget WHERE TenantId=@TenantId AND EventType=@EventType",new { TenantId=tenantId,EventType=eventType });
        void Replay(string script)
        {
            var builder=sqlServer?DeployChanges.To.SqlDatabase(connection)
                :DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection,MySqlGuidStorageMode.Binary16,allowUserVariables:true));
            var result=builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,name =>
                    name.Contains(sqlServer?".Migrations.SqlServer.":".Migrations.MySql.",StringComparison.Ordinal) && name.EndsWith(script,StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful,result.Error?.ToString()); Assert.HasCount(1,result.Scripts);
        }
    }
}
