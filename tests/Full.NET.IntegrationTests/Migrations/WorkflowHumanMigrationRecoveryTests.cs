using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>人工活动恢复过程采用统一闭合格式后，双库验证首次部署、缺项修复和无遗留过程。</summary>
[TestClass]
public sealed class WorkflowHumanMigrationRecoveryTests
{
    [TestMethod]
    public async Task Human_activity_migrations_recover_with_sql_server() =>
        await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Human_activity_migrations_recover_with_mysql() =>
        await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        await MigrateAsync(provider, connection);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_claim_due ON fn_workflow_claim");
        var constraints = new[] { (Table: "fn_workflow_claim", Name: "ck_fn_workflow_claim_policy"),
            (Table: "fn_workflow_manual_reminder", Name: "ck_fn_workflow_manual_reminder_bounds"),
            (Table: "fn_workflow_starter_selection", Name: "ck_fn_workflow_starter_selection_hash"),
            (Table: "fn_workflow_domain_audit", Name: "ck_fn_workflow_domain_audit_actor"),
            (Table: "fn_workflow_approval_group", Name: "ck_fn_workflow_approval_group_state_306"),
            (Table: "fn_workflow_step", Name: "ck_fn_workflow_step_approval_groups_306") };
        foreach (var constraint in constraints)
            await db.ExecuteAsync($"ALTER TABLE {constraint.Table} DROP {(provider == DatabaseProvider.SqlServer ? "CONSTRAINT" : "CHECK")} {constraint.Name}");
        var journal = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        foreach (var script in new[] { "302_WorkflowClaimPools.sql", "303_WorkflowManualReminders.sql", "304_WorkflowStarterSelections.sql", "305_WorkflowSystemAssigneeAudit.sql", "306_WorkflowSystemApprovalGroups.sql" })
            await db.ExecuteAsync($"DELETE FROM {journal} WHERE ScriptName LIKE @script", new { script = "%" + script });
        await MigrateAsync(provider, connection);
        foreach (var constraint in constraints)
        {
            var count = provider == DatabaseProvider.SqlServer
                ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name=@name AND parent_object_id=OBJECT_ID(@table)", new { name = constraint.Name, table = constraint.Table })
                : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME=@table AND CONSTRAINT_NAME=@name AND CONSTRAINT_TYPE='CHECK'", new { name = constraint.Name, table = constraint.Table });
            Assert.AreEqual(1, count, constraint.Name);
        }
        Assert.AreEqual(1, provider == DatabaseProvider.SqlServer
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_claim') AND name='ix_fn_workflow_claim_due'")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_claim' AND INDEX_NAME='ix_fn_workflow_claim_due'"));
        // 模拟新约束已添加、旧约束还未来得及移除且尚未记账；重跑不能停留在互相冲突的双重限制。
        await db.ExecuteAsync("ALTER TABLE fn_workflow_approval_group ADD CONSTRAINT ck_fn_workflow_approval_group_state CHECK (SlotCount>=1)");
        await db.ExecuteAsync("ALTER TABLE fn_workflow_step ADD CONSTRAINT ck_fn_workflow_step_approval_groups CHECK (ApprovalSlotCount IS NULL OR ApprovalSlotCount>=COALESCE(ApprovalGroupCount,0))");
        await db.ExecuteAsync($"DELETE FROM {journal} WHERE ScriptName LIKE @script", new { script = "%306_WorkflowSystemApprovalGroups.sql" });
        await MigrateAsync(provider, connection);
        Assert.AreEqual(0, provider == DatabaseProvider.SqlServer
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('ck_fn_workflow_approval_group_state','ck_fn_workflow_step_approval_groups')")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_TYPE='CHECK' AND CONSTRAINT_NAME IN ('ck_fn_workflow_approval_group_state','ck_fn_workflow_step_approval_groups')"));
        if (provider == DatabaseProvider.MySql)
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME IN @names", new {
                names = new[] { "fn_workflow_ensure_claim_facts", "fn_workflow_restore_manual_reminders_303", "fn_workflow_restore_starter_selection_304", "fn_workflow_restore_system_audit_305", "fn_workflow_restore_system_groups_306" } }));
    }

    internal static async Task MigrateAsync(DatabaseProvider provider, string connection)
    {
        // 新库必须走正式的已发布迁移预处理器，旧的 114 专用兼容器只适用于已有前置结构的恢复场景。
        await new DbUpMigrationRunner(Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = connection,
                MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 }),
            NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions()).MigrateAsync();
    }
}
