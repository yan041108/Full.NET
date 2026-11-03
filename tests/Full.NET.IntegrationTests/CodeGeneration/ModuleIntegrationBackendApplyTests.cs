extern alias codegencli;

using CodeGenerationCli = codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>分步写命令退役后不能继续改动工作区；真实完整 CLI 的编译、提交与回滚由整链测试覆盖。</summary>
[TestClass]
public sealed class ModuleIntegrationBackendApplyTests
{
    [TestMethod]
    [DataRow("apply-module-integration")]
    [DataRow("apply-module-entry-integration")]
    [DataRow("apply-composition-integration")]
    [DataRow("apply-client-route-integration")]
    public async Task Retired_stage_command_preserves_workspace_and_points_to_complete_integration(string command)
    {
        var root = Directory.CreateTempSubdirectory("fullnet-retired-codegen-stage-").FullName;
        try
        {
            var schema = Path.Combine(root, "schema.json");
            var target = Path.Combine(root, "target.json");
            await File.WriteAllTextAsync(schema, """
                {"ownerKey":"acme","moduleKey":"catalog","entityKey":"product","databaseTableName":"acme_catalog_product",
                 "rootNamespace":"Acme.Modules.Catalog","clrTypeName":"Product","apiResourceName":"products","permissionResourceName":"products",
                 "isTenantScoped":false,"hasVersion":true,"columns":[
                   {"databaseName":"Id","clrPropertyName":"Id","jsonPropertyName":"id","scalarType":"Uuid"},
                   {"databaseName":"Name","clrPropertyName":"Name","jsonPropertyName":"name","scalarType":"String","maxLength":200},
                   {"databaseName":"IsActive","clrPropertyName":"IsActive","jsonPropertyName":"isActive","scalarType":"Boolean"},
                   {"databaseName":"Version","clrPropertyName":"Version","jsonPropertyName":"version","scalarType":"Int64"}]}
                """);
            await File.WriteAllTextAsync(target, """
                {"moduleName":"Catalog","moduleProjectPath":"module/Catalog.csproj","moduleEntryPointPath":"module/CatalogModule.cs",
                 "compositionProjectPath":"composition/Composition.csproj","compositionCatalogPath":"composition/Catalog.cs","vueRouterPath":"ui/router.ts"}
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Manual.cs"), "// 人工文件必须保持。\n");
            var originals = Directory.EnumerateFiles(root).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.AreEqual(64, await CodeGenerationCli.RunAsync([command, "--schema", schema, "--repository", root, "--target", target], output, error));
            StringAssert.Contains(error.ToString(), "分步接入写盘已移除");
            StringAssert.Contains(error.ToString(), "apply-host-integration");
            Assert.AreEqual(string.Empty, output.ToString());
            Assert.AreEqual(originals.Count, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
            foreach (var original in originals) Assert.AreEqual(original.Value, File.ReadAllText(Path.Combine(root, original.Key!)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
