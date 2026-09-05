using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

/// <summary>锁定 Workflow 成对迁移的表合同、模块边界和暂停占用契约。</summary>
[TestClass]
public sealed partial class WorkflowMigrationContractTests
{
    private static readonly string[] ExpectedTables =
    [
        "fn_workflow_action_record",
        "fn_workflow_cc",
        "fn_workflow_definition",
        "fn_workflow_definition_draft",
        "fn_workflow_definition_version",
        "fn_workflow_domain_audit",
        "fn_workflow_execution_log",
        "fn_workflow_form_definition",
        "fn_workflow_form_submission",
        "fn_workflow_form_version",
        "fn_workflow_instance",
        "fn_workflow_step",
        "fn_workflow_todo",
    ];

    [TestMethod]
    public void Workflow_102_migrations_publish_equivalent_owned_table_contracts()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sqlServer = ReadMigration(root, "SqlServer");
        var mySql = ReadMigration(root, "MySql");

        CollectionAssert.AreEqual(ExpectedTables, ReadTables(sqlServer));
        CollectionAssert.AreEqual(ExpectedTables, ReadTables(mySql));
        StringAssert.Contains(sqlServer, "uniqueidentifier");
        StringAssert.Contains(mySql, "BINARY(16)");
        StringAssert.Contains(sqlServer, "UX_fn_workflow_instance_ActiveBusinessKey");
        StringAssert.Contains(mySql, "UX_fn_workflow_instance_ActiveBusinessKey");
        StringAssert.Contains(sqlServer, "TR_fn_workflow_definition_version_Immutable");
        StringAssert.Contains(mySql, "TR_fn_workflow_definition_version_Immutable");
    }

    /// <summary>109 必须把暂停实例纳入业务唯一占用，且双库表达式保持等价。</summary>
    [TestMethod]
    public void Workflow_109_migrations_extend_active_business_key_occupancy_to_suspended()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sqlServer = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations",
            "SqlServer", "109_WorkflowSuspendedInstanceOccupancy.sql"));
        var mySql = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations",
            "MySql", "109_WorkflowSuspendedInstanceOccupancy.sql"));

        StringAssert.Contains(sqlServer, "StatusKey IN ('active', 'suspended')");
        StringAssert.Contains(mySql, "StatusKey IN ('active', 'suspended')");
        StringAssert.Contains(sqlServer, "UX_fn_workflow_instance_ActiveBusinessKey");
        StringAssert.Contains(mySql, "UX_fn_workflow_instance_ActiveBusinessKey");
        StringAssert.Contains(sqlServer, "占用中的实例业务唯一键");
        StringAssert.Contains(mySql, "占用中的实例业务唯一键");
    }

    /// <summary>110 必须成对新增恢复任务表，并用占用键保证未关闭任务唯一。</summary>
    [TestMethod]
    public void Workflow_110_migrations_publish_recovery_task_occupancy_contract()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sqlServer = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations",
            "SqlServer", "110_WorkflowRecoveryTask.sql"));
        var mySql = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations",
            "MySql", "110_WorkflowRecoveryTask.sql"));

        StringAssert.Contains(sqlServer, "fn_workflow_recovery_task");
        StringAssert.Contains(mySql, "fn_workflow_recovery_task");
        StringAssert.Contains(sqlServer, "UX_fn_workflow_recovery_task_OpenOccupancy");
        StringAssert.Contains(mySql, "UX_fn_workflow_recovery_task_OpenOccupancy");
        StringAssert.Contains(sqlServer, "LeaseGeneration");
        StringAssert.Contains(mySql, "LeaseGeneration");
        StringAssert.Contains(sqlServer, "pending', 'failed', 'dead_lettered");
        StringAssert.Contains(mySql, "pending', 'failed', 'dead_lettered");
        StringAssert.Contains(sqlServer, "工作流恢复任务表");
        StringAssert.Contains(mySql, "工作流恢复任务表");
    }

    [TestMethod]
    public void Workflow_102_migrations_do_not_reference_foreign_module_tables()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sql = ReadMigration(root, "SqlServer") + ReadMigration(root, "MySql");
        var foreignTables = TableRegex().Matches(sql)
            .Select(match => match.Value)
            .Where(table => !table.StartsWith("fn_workflow_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            foreignTables,
            "Workflow 迁移禁止创建或引用其他模块的数据表。" + string.Join(',', foreignTables));
    }

    private static string ReadMigration(string root, string provider) =>
        File.ReadAllText(Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Migrations.DbUp",
            "Migrations",
            provider,
            "102_WorkflowFirstVerticalSlice.sql"));

    private static string[] ReadTables(string sql) => TableRegex().Matches(sql)
        .Select(match => match.Value)
        .Where(table => table.StartsWith("fn_workflow_", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    [GeneratedRegex(@"\bfn_[a-z0-9]+_[a-z0-9_]+\b")]
    private static partial Regex TableRegex();
}
