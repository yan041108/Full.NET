using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

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
