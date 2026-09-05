using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class WorkflowModuleArchitectureTests
{
    /// <summary>验证 Workflow 只引用批准的 Building Block、Identity Contract 与自身稳定契约。</summary>
    [TestMethod]
    public void Workflow_module_references_only_approved_building_blocks_and_identity_contracts()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var projectPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Workflow",
            "Full.NET.Modules.Workflow.csproj");
        var document = XDocument.Load(projectPath);
        var references = document.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Full.NET.Abstractions",
                "Full.NET.Data.Abstractions",
                "Full.NET.Data.Dapper",
                "Full.NET.Hosting",
                "Full.NET.Modularity",
                "Full.NET.Modules.Identity.Contracts",
                "Full.NET.Modules.Workflow.Contracts",
            },
            references);
        Assert.IsFalse(document.Descendants("PackageReference").Any());
    }

    [TestMethod]
    public void Workflow_module_does_not_use_runtime_dynamic_code_or_concrete_sql_drivers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleRoot = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Workflow");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "System.Reflection.Emit",
            "Assembly.Load",
            "Activator.CreateInstance",
            "Microsoft.Data.SqlClient",
            "MySqlConnector",
            "Dapper.SqlMapper",
        })
        {
            Assert.IsFalse(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"Workflow 模块包含禁止的动态或具体驱动依赖：{forbidden}");
        }
    }

    [TestMethod]
    public void Workflow_persistence_is_parameterized_revision_safe_and_aot_closed()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var persistenceRoot = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Workflow",
            "Persistence");
        var sql = File.ReadAllText(Path.Combine(persistenceRoot, "WorkflowSql.cs"));
        var parameters = File.ReadAllText(
            Path.Combine(persistenceRoot, "WorkflowSqlParameters.cs"));
        var materializers = File.ReadAllText(
            Path.Combine(persistenceRoot, "WorkflowDapperAotMaterializerContributor.cs"));
        var module = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Workflow",
            "WorkflowModule.cs"));

        foreach (var statementName in new[]
        {
            "workflow.definition.find_by_key",
            "workflow.definition_draft.find_by_definition",
            "workflow.definition_version.find_by_id",
            "workflow.form_definition.find_by_key",
            "workflow.form_version.find_by_id",
            "workflow.instance.find_by_id",
            "workflow.todo.find_by_id",
            "workflow.todo.complete_with_revision",
        })
        {
            StringAssert.Contains(sql, statementName);
        }

        Assert.IsFalse(sql.Contains("SELECT *", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(sql, "Revision = Revision + 1");
        StringAssert.Contains(sql, "Revision = @Revision");
        StringAssert.Contains(parameters, "Dictionary<string, object?>");
        StringAssert.Contains(parameters, "StringComparer.Ordinal");
        foreach (var recordName in new[]
        {
            "WorkflowDefinitionRecord",
            "WorkflowDefinitionDraftRecord",
            "WorkflowDefinitionVersionRecord",
            "WorkflowFormDefinitionRecord",
            "WorkflowFormVersionRecord",
            "WorkflowInstanceRecord",
            "WorkflowTodoRecord",
        })
        {
            StringAssert.Contains(materializers, $"registrar.Register<{recordName}>");
        }

        StringAssert.Contains(module, "WorkflowDapperAotMaterializerContributor");
    }
}
