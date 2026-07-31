using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationCliTests
{
    [TestMethod]
    public async Task Preview_valid_schema_reports_creates_without_writing()
    {
        using var fixture = CliFixture.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
            ],
            output,
            error);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(
            output.ToString(),
            "Create backend/ProductContracts.g.cs");
        Assert.AreEqual(string.Empty, error.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                fixture.WorkspacePath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_valid_schema_writes_and_repeats_as_unchanged()
    {
        using var fixture = CliFixture.Create();
        using var applyOutput = new StringWriter();
        using var previewOutput = new StringWriter();
        using var error = new StringWriter();

        var applyExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
                "--apply",
            ],
            applyOutput,
            error);
        var previewExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
            ],
            previewOutput,
            error);

        Assert.AreEqual(0, applyExitCode);
        Assert.AreEqual(0, previewExitCode);
        StringAssert.Contains(
            applyOutput.ToString(),
            "Create backend/ProductContracts.g.cs");
        Assert.AreEqual(
            13,
            previewOutput
                .ToString()
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries)
                .Count(line =>
                    line.StartsWith(
                        "Unchanged ",
                        StringComparison.Ordinal)));
        Assert.IsTrue(File.Exists(Path.Combine(
            fixture.WorkspacePath,
            "backend",
            "ProductContracts.g.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(
            fixture.WorkspacePath,
            "templates",
            "migrations",
            "SqlServer",
            "CreateProduct.sql.template")));
        Assert.IsTrue(File.Exists(Path.Combine(
            fixture.WorkspacePath,
            GenerationWorkspaceStore.ManifestRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar))));
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Apply_handwritten_conflict_returns_two_without_other_writes()
    {
        using var fixture = CliFixture.Create();
        fixture.WriteWorkspaceFile(
            "backend/ProductContracts.g.cs",
            "handwritten");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
                "--apply",
            ],
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(
            output.ToString(),
            "Conflict backend/ProductContracts.g.cs");
        Assert.AreEqual(
            "handwritten",
            fixture.ReadWorkspaceFile(
                "backend/ProductContracts.g.cs"));
        Assert.AreEqual(
            1,
            Directory.GetFiles(
                fixture.WorkspacePath,
                "*",
                SearchOption.AllDirectories).Length);
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task Invalid_arguments_or_schema_return_usage_error()
    {
        using var fixture = CliFixture.Create();
        using var argumentOutput = new StringWriter();
        using var argumentError = new StringWriter();
        using var schemaOutput = new StringWriter();
        using var schemaError = new StringWriter();
        using var enumOutput = new StringWriter();
        using var enumError = new StringWriter();

        var argumentExitCode = await CodeGenerationCli.RunAsync(
            ["--unknown"],
            argumentOutput,
            argumentError);
        File.WriteAllText(
            fixture.SchemaPath,
            ValidSchemaJson.Replace(
                "\"ownerKey\": \"acme\",",
                "\"ownerKey\": \"acme\", \"unexpected\": true,",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        var schemaExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
            ],
            schemaOutput,
            schemaError);
        File.WriteAllText(
            fixture.SchemaPath,
            ValidSchemaJson.Replace(
                "\"scalarType\": \"Uuid\"",
                "\"scalarType\": 1",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        var enumExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
            ],
            enumOutput,
            enumError);

        Assert.AreEqual(64, argumentExitCode);
        StringAssert.Contains(
            argumentError.ToString(),
            "--schema");
        Assert.AreEqual(64, schemaExitCode);
        StringAssert.Contains(
            schemaError.ToString(),
            "Schema");
        Assert.AreEqual(64, enumExitCode);
        StringAssert.Contains(
            enumError.ToString(),
            "Schema");
        Assert.AreEqual(string.Empty, argumentOutput.ToString());
        Assert.AreEqual(string.Empty, schemaOutput.ToString());
        Assert.AreEqual(string.Empty, enumOutput.ToString());
    }

    [TestMethod]
    public async Task Import_database_requires_existing_connection_environment_variable()
    {
        using var fixture = CliFixture.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environmentVariable =
            $"FULLNET_CODEGEN_MISSING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariable, null);

        var exitCode = await CodeGenerationCli.RunAsync(
            DatabaseImportArguments(
                fixture.WorkspacePath,
                environmentVariable),
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(
            error.ToString(),
            "--connection-env");
        Assert.IsFalse(
            error.ToString().Contains(
                environmentVariable,
                StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                fixture.WorkspacePath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Import_database_rejects_inline_connection_string_without_echoing_secret()
    {
        using var fixture = CliFixture.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();
        const string secret = "Server=secret-host;Password=secret-value";

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "import-database",
                "--connection-string",
                secret,
            ],
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(
            error.ToString(),
            "--connection-string");
        Assert.IsFalse(
            error.ToString().Contains(
                secret,
                StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task List_database_tables_requires_existing_connection_environment_variable()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var environmentVariable =
            $"FULLNET_CODEGEN_CATALOG_MISSING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariable, null);

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "list-database-tables",
                "--provider",
                "sqlserver",
                "--connection-env",
                environmentVariable,
            ],
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(
            error.ToString(),
            "--connection-env 指向的环境变量不存在或为空。");
        Assert.IsFalse(
            error.ToString().Contains(
                environmentVariable,
                StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task List_database_tables_rejects_generation_workspace_options()
    {
        using var fixture = CliFixture.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "list-database-tables",
                "--provider",
                "mysql",
                "--connection-env",
                "FULLNET_CODEGEN_UNUSED",
                "--workspace",
                fixture.WorkspacePath,
            ],
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(
            error.ToString(),
            "未知参数：--workspace");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                fixture.WorkspacePath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Batch_mapping_loads_only_explicit_table_semantics()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        File.WriteAllText(
            mappingPath,
            ValidBatchMappingJson,
            new UTF8Encoding(false, true));

        var mappings = await DatabaseBatchMappingDocument.LoadAsync(
            mappingPath,
            CancellationToken.None);

        Assert.HasCount(1, mappings);
        Assert.AreEqual("acme", mappings[0].OwnerKey);
        Assert.AreEqual("catalog", mappings[0].ModuleKey);
        Assert.AreEqual("product", mappings[0].EntityKey);
        Assert.AreEqual(
            FullNetCrudDataScope.TenantRequired,
            mappings[0].DataScope);
        Assert.IsTrue(mappings[0].HasVersion);
    }

    [TestMethod]
    public async Task Batch_mapping_rejects_duplicate_tables_and_integer_scope()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        var duplicateRoot =
            JsonNode.Parse(ValidBatchMappingJson)!.AsObject();
        var duplicateTables = duplicateRoot["tables"]!.AsArray();
        duplicateTables.Add(duplicateTables[0]!.DeepClone());
        File.WriteAllText(
            mappingPath,
            duplicateRoot.ToJsonString(),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            DatabaseBatchMappingDocument.LoadAsync(
                mappingPath,
                CancellationToken.None));

        var integerScopeRoot =
            JsonNode.Parse(ValidBatchMappingJson)!.AsObject();
        integerScopeRoot["tables"]![0]!["dataScope"] = 1;
        File.WriteAllText(
            mappingPath,
            integerScopeRoot.ToJsonString(),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(() =>
            DatabaseBatchMappingDocument.LoadAsync(
                mappingPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Preview_database_batch_rejects_empty_mapping_and_unknown_json()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        File.WriteAllText(
            mappingPath,
            """{"tables":[]}""",
            new UTF8Encoding(false, true));
        using var emptyOutput = new StringWriter();
        using var emptyError = new StringWriter();

        var emptyExitCode = await CodeGenerationCli.RunAsync(
            DatabaseBatchArguments(
                fixture.WorkspacePath,
                mappingPath,
                "FULLNET_CODEGEN_UNUSED"),
            emptyOutput,
            emptyError);

        File.WriteAllText(
            mappingPath,
            ValidBatchMappingJson.Replace(
                "\"tables\":",
                "\"unexpected\": true, \"tables\":",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        using var unknownOutput = new StringWriter();
        using var unknownError = new StringWriter();
        var unknownExitCode = await CodeGenerationCli.RunAsync(
            DatabaseBatchArguments(
                fixture.WorkspacePath,
                mappingPath,
                "FULLNET_CODEGEN_UNUSED"),
            unknownOutput,
            unknownError);

        Assert.AreEqual(64, emptyExitCode);
        StringAssert.Contains(emptyError.ToString(), "tables");
        Assert.AreEqual(64, unknownExitCode);
        StringAssert.Contains(unknownError.ToString(), "unexpected");
        Assert.AreEqual(string.Empty, emptyOutput.ToString());
        Assert.AreEqual(string.Empty, unknownOutput.ToString());
    }

    [TestMethod]
    public async Task Preview_database_batch_rejects_apply_before_reading_connection()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        File.WriteAllText(
            mappingPath,
            ValidBatchMappingJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var arguments = DatabaseBatchArguments(
                fixture.WorkspacePath,
                mappingPath,
                "FULLNET_CODEGEN_UNUSED")
            .Append("--apply")
            .ToArray();

        var exitCode = await CodeGenerationCli.RunAsync(
            arguments,
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(error.ToString(), "--apply");
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task Apply_database_batch_requires_existing_connection_environment_variable()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        File.WriteAllText(
            mappingPath,
            ValidBatchMappingJson,
            new UTF8Encoding(false, true));
        var environmentVariable =
            $"FULLNET_CODEGEN_BATCH_MISSING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariable, null);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            DatabaseBatchArguments(
                fixture.WorkspacePath,
                mappingPath,
                environmentVariable,
                command: "apply-database-batch"),
            output,
            error);

        Assert.AreEqual(64, exitCode);
        var firstErrorLine = error.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries)[0];
        StringAssert.Contains(firstErrorLine, "--connection-env");
        Assert.IsFalse(
            error.ToString().Contains(
                environmentVariable,
                StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                fixture.WorkspacePath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Plan_module_integration_reports_impacts_without_writing_repository()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        WriteRepositoryFile(
            repositoryPath,
            "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
            """<Project Sdk="Microsoft.NET.Sdk" />""");
        WriteRepositoryFile(
            repositoryPath,
            "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
            "namespace Acme.Modules.Catalog; public sealed class CatalogModule { }");
        WriteRepositoryFile(
            repositoryPath,
            "src/Composition/Acme.Composition/Acme.Composition.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\Modules\Acme.Modules.Catalog\Acme.Modules.Catalog.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteRepositoryFile(
            repositoryPath,
            "src/Composition/Acme.Composition/ModuleCatalog.cs",
            """
            using Acme.Modules.Catalog;
            namespace Acme.Composition;
            internal static class ModuleCatalog
            {
                private static object[] Modules => [new CatalogModule()];
            }
            """);
        WriteRepositoryFile(
            repositoryPath,
            "ui/admin/src/router/index.ts",
            "export const routes = [];");
        WriteRepositoryFile(
            repositoryPath,
            "ui/admin-layui/js/core/route-controllers.js",
            "export const routes = [];");
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        var before = CaptureRepository(repositoryPath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath),
            output,
            error);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(
            output.ToString(),
            "ChangeRequired BackendArtifacts src/Modules/Acme.Modules.Catalog/Generated");
        StringAssert.Contains(
            output.ToString(),
            "Satisfied CompositionProject");
        StringAssert.Contains(
            output.ToString(),
            "ManualReview VueRoute");
        Assert.AreEqual(string.Empty, error.ToString());
        CollectionAssert.AreEquivalent(
            before.ToArray(),
            CaptureRepository(repositoryPath).ToArray());
    }

    [TestMethod]
    public async Task Plan_module_integration_reports_missing_targets_as_blocked()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath),
            output,
            error);

        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(
            output.ToString(),
            "Blocked BackendArtifacts");
        StringAssert.Contains(
            output.ToString(),
            "Blocked VueRoute");
        Assert.AreEqual(string.Empty, error.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Plan_module_integration_rejects_apply_unknown_json_and_unsafe_paths()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var applyOutput = new StringWriter();
        using var applyError = new StringWriter();

        var applyExitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                    fixture.SchemaPath,
                    repositoryPath,
                    targetPath)
                .Append("--apply")
                .ToArray(),
            applyOutput,
            applyError);

        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson.Replace(
                "\"moduleName\":",
                "\"unexpected\": true, \"moduleName\":",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        using var unknownOutput = new StringWriter();
        using var unknownError = new StringWriter();
        var unknownExitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath),
            unknownOutput,
            unknownError);

        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson.Replace(
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "../outside.csproj",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        using var unsafeOutput = new StringWriter();
        using var unsafeError = new StringWriter();
        var unsafeExitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath),
            unsafeOutput,
            unsafeError);

        Assert.AreEqual(64, applyExitCode);
        StringAssert.Contains(applyError.ToString(), "--apply");
        Assert.AreEqual(64, unknownExitCode);
        StringAssert.Contains(unknownError.ToString(), "unexpected");
        Assert.AreEqual(64, unsafeExitCode);
        StringAssert.Contains(unsafeError.ToString(), "路径");
        Assert.AreEqual(string.Empty, applyOutput.ToString());
        Assert.AreEqual(string.Empty, unknownOutput.ToString());
        Assert.AreEqual(string.Empty, unsafeOutput.ToString());
    }

    [TestMethod]
    public async Task Validate_module_integration_reports_missing_project_without_writes()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "validate-module-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "模块项目不存在");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Validate_module_integration_rejects_apply()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                    fixture.SchemaPath,
                    repositoryPath,
                    targetPath,
                    command: "validate-module-integration")
                .Append("--apply")
                .ToArray(),
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(error.ToString(), "--apply");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Validate_module_integration_rejects_module_namespace_mismatch()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            fixture.SchemaPath,
            ValidSchemaJson.Replace(
                "\"rootNamespace\": \"Acme.Modules.Catalog\"",
                "\"rootNamespace\": \"Acme.Modules.Sales\"",
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "validate-module-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "根命名空间");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_module_integration_reports_missing_project_without_writes()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "apply-module-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "模块项目不存在");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_module_entry_integration_reports_missing_project_without_writes()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "apply-module-entry-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "模块项目不存在");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_composition_integration_reports_missing_module_project_without_writes()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "apply-composition-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "模块项目不存在");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_client_route_integration_requires_explicit_descriptor_without_writes()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(
            fixture.RootPath,
            "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            ModuleIntegrationArguments(
                fixture.SchemaPath,
                repositoryPath,
                targetPath,
                command: "apply-client-route-integration"),
            output,
            error);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(error.ToString(), "clientRoute");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Apply_module_integration_rejects_apply_switch()
    {
        using var fixture = CliFixture.Create();
        var repositoryPath = Path.Combine(fixture.RootPath, "repository");
        var targetPath = Path.Combine(fixture.RootPath, "integration-target.json");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(
            targetPath,
            ValidIntegrationTargetJson,
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                .. ModuleIntegrationArguments(
                    fixture.SchemaPath,
                    repositoryPath,
                    targetPath,
                    command: "apply-module-integration"),
                "--apply",
            ],
            output,
            error);

        Assert.AreEqual(64, exitCode);
        StringAssert.Contains(error.ToString(), "--apply");
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            0,
            Directory.GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Explicit_json_data_scope_is_loaded_without_legacy_boolean()
    {
        using var fixture = CliFixture.Create();
        File.WriteAllText(
            fixture.SchemaPath,
            ExplicitScopeSchemaJson("HostOnly"),
            new UTF8Encoding(false, true));

        var schema = await CrudSchemaDocument.LoadAsync(
            fixture.SchemaPath,
            CancellationToken.None);

        Assert.AreEqual(FullNetCrudDataScope.HostOnly, schema.DataScope);
        Assert.IsFalse(schema.IsTenantScoped);
    }

    [TestMethod]
    public async Task Explicit_json_entity_capabilities_are_loaded_without_legacy_has_version()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("hasVersion");
        AddExplicitCapabilities(root);
        AddCapabilityColumns(root["columns"]!.AsArray());
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        var schema = await CrudSchemaDocument.LoadAsync(
            fixture.SchemaPath,
            CancellationToken.None);

        Assert.IsFalse(schema.UsesLegacyEntityCapabilities);
        Assert.AreEqual(
            FullNetCrudDeleteMode.SoftDelete,
            schema.EntityCapabilities.DeleteMode);
        Assert.AreEqual(
            FullNetCrudOwnershipMode.OrganizationUnit,
            schema.EntityCapabilities.OwnershipMode);
        Assert.IsTrue(schema.EntityCapabilities.HasCreatedAudit);
        Assert.IsTrue(schema.EntityCapabilities.HasUpdatedAudit);
        Assert.IsTrue(schema.EntityCapabilities.HasDeletedAudit);
        Assert.IsTrue(schema.EntityCapabilities.HasVersion);
    }

    [TestMethod]
    public async Task Explicit_json_tree_scene_requires_and_preserves_parent_id()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("hasVersion");
        AddHardDeleteCapabilities(root);
        root["scene"] = "tree";
        root["columns"]!.AsArray().Add(new JsonObject
        {
            ["databaseName"] = "ParentId",
            ["clrPropertyName"] = "ParentId",
            ["jsonPropertyName"] = "parentId",
            ["scalarType"] = "uuid",
            ["isNullable"] = true,
        });
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        var schema = await CrudSchemaDocument.LoadAsync(
            fixture.SchemaPath,
            CancellationToken.None);

        Assert.AreEqual(FullNetCrudScene.Tree, schema.Scene);
        Assert.HasCount(0, schema.Relationships);

        root["scene"] = "TREE";
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));
        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Explicit_json_relationship_scene_preserves_both_sides()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("hasVersion");
        AddHardDeleteCapabilities(root);
        root["scene"] = "master.detail";
        root["relationships"] = new JsonArray
        {
            RelationshipJson("tenant.required"),
        };
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        var schema = await CrudSchemaDocument.LoadAsync(
            fixture.SchemaPath,
            CancellationToken.None);

        Assert.AreEqual(FullNetCrudScene.MasterDetail, schema.Scene);
        var relationship = schema.Relationships.Single();
        Assert.AreEqual("product", relationship.PrincipalEntityKey);
        Assert.AreEqual("product_item", relationship.DependentEntityKey);
        Assert.AreEqual("ProductId", relationship.DependentColumnName);
    }

    [TestMethod]
    public async Task Explicit_json_relationship_scene_rejects_cross_scope()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("hasVersion");
        AddHardDeleteCapabilities(root);
        root["scene"] = "master.detail";
        root["relationships"] = new JsonArray
        {
            RelationshipJson("host.only"),
        };
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Legacy_has_version_rejects_scene_fields()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root["scene"] = "tree";
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Explicit_json_entity_capabilities_require_every_member()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("hasVersion");
        AddExplicitCapabilities(root);
        root["entityCapabilities"]!.AsObject().Remove("hasDeletedAudit");
        AddCapabilityColumns(root["columns"]!.AsArray());
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Explicit_json_entity_capabilities_reject_legacy_has_version()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        AddExplicitCapabilities(root);
        AddCapabilityColumns(root["columns"]!.AsArray());
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Explicit_json_entity_capabilities_reject_null_legacy_discriminator()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root["hasVersion"] = null;
        AddExplicitCapabilities(root);
        AddCapabilityColumns(root["columns"]!.AsArray());
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            CrudSchemaDocument.LoadAsync(
                fixture.SchemaPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Batch_mapping_loads_explicit_entity_capabilities()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        var root = JsonNode.Parse(ValidBatchMappingJson)!.AsObject();
        var table = root["tables"]![0]!.AsObject();
        table.Remove("hasVersion");
        AddExplicitCapabilities(table);
        File.WriteAllText(
            mappingPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        var mappings = await DatabaseBatchMappingDocument.LoadAsync(
            mappingPath,
            CancellationToken.None);

        Assert.HasCount(1, mappings);
        Assert.IsFalse(mappings[0].UsesLegacyEntityCapabilities);
        Assert.AreEqual(
            FullNetCrudDeleteMode.SoftDelete,
            mappings[0].EntityCapabilities.DeleteMode);
        Assert.AreEqual(
            FullNetCrudOwnershipMode.OrganizationUnit,
            mappings[0].EntityCapabilities.OwnershipMode);
    }

    [TestMethod]
    public async Task Batch_mapping_rejects_null_legacy_discriminator_with_capabilities()
    {
        using var fixture = CliFixture.Create();
        var mappingPath = Path.Combine(fixture.RootPath, "batch-mapping.json");
        var root = JsonNode.Parse(ValidBatchMappingJson)!.AsObject();
        var table = root["tables"]![0]!.AsObject();
        table["hasVersion"] = null;
        AddExplicitCapabilities(table);
        File.WriteAllText(
            mappingPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            DatabaseBatchMappingDocument.LoadAsync(
                mappingPath,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Decimal_json_shape_is_preserved_by_strict_schema_loading()
    {
        using var fixture = CliFixture.Create();
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        var columns = root["columns"]!.AsArray();
        var isActive = columns.Single(column =>
            column!["databaseName"]!.GetValue<string>() == "IsActive");
        columns.Insert(
            columns.IndexOf(isActive),
            new JsonObject
            {
                ["databaseName"] = "Price",
                ["clrPropertyName"] = "Price",
                ["jsonPropertyName"] = "price",
                ["scalarType"] = "Decimal",
                ["numericPrecision"] = 18,
                ["numericScale"] = 2,
            });
        File.WriteAllText(
            fixture.SchemaPath,
            root.ToJsonString(new() { WriteIndented = true }),
            new UTF8Encoding(false, true));

        var schema = await CrudSchemaDocument.LoadAsync(
            fixture.SchemaPath,
            CancellationToken.None);
        var price = schema.Columns.Single(column =>
            column.DatabaseName == "Price");

        Assert.AreEqual(18, price.NumericPrecision);
        Assert.AreEqual(2, price.NumericScale);
    }

    [TestMethod]
    public async Task Conflicting_json_scope_fields_return_usage_error()
    {
        using var fixture = CliFixture.Create();
        File.WriteAllText(
            fixture.SchemaPath,
            ValidSchemaJson.Replace(
                "\"isTenantScoped\": true,",
                """
                "isTenantScoped": true,
                  "dataScope": "HostOnly",
                """,
                StringComparison.Ordinal),
            new UTF8Encoding(false, true));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                fixture.SchemaPath,
                "--workspace",
                fixture.WorkspacePath,
            ],
            output,
            error);

        Assert.AreEqual(64, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "Schema");
    }

    [TestMethod]
    public async Task Import_database_accepts_explicit_scope_and_rejects_mixed_scope_flags()
    {
        using var fixture = CliFixture.Create();
        var environmentVariable =
            $"FULLNET_CODEGEN_MISSING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariable, null);
        var explicitArguments = DatabaseImportArguments(
                fixture.WorkspacePath,
                environmentVariable)
            .ToList();
        var tenantScopedIndex = explicitArguments.IndexOf("--tenant-scoped");
        explicitArguments.RemoveRange(tenantScopedIndex, 2);
        explicitArguments.InsertRange(
            explicitArguments.IndexOf("--has-version"),
            ["--data-scope", "host"]);
        using var explicitOutput = new StringWriter();
        using var explicitError = new StringWriter();

        var explicitExitCode = await CodeGenerationCli.RunAsync(
            explicitArguments.ToArray(),
            explicitOutput,
            explicitError);
        var mixedArguments = DatabaseImportArguments(
                fixture.WorkspacePath,
                environmentVariable)
            .ToList();
        mixedArguments.InsertRange(
            mixedArguments.IndexOf("--has-version"),
            ["--data-scope", "host"]);
        using var mixedOutput = new StringWriter();
        using var mixedError = new StringWriter();
        var mixedExitCode = await CodeGenerationCli.RunAsync(
            mixedArguments.ToArray(),
            mixedOutput,
            mixedError);

        Assert.AreEqual(64, explicitExitCode);
        StringAssert.Contains(explicitError.ToString(), "--connection-env");
        Assert.AreEqual(64, mixedExitCode);
        StringAssert.Contains(mixedError.ToString(), "作用域");
    }

    [TestMethod]
    public async Task Missing_required_semantic_fields_return_usage_error()
    {
        using var fixture = CliFixture.Create();
        var requiredFields = new[]
        {
            "\"isTenantScoped\": true,",
            "\"hasVersion\": true,",
            "\"scalarType\": \"Uuid\"",
        };

        foreach (var requiredField in requiredFields)
        {
            File.WriteAllText(
                fixture.SchemaPath,
                ValidSchemaJson.Replace(
                    requiredField,
                    string.Empty,
                    StringComparison.Ordinal),
                new UTF8Encoding(false, true));
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                [
                    "--schema",
                    fixture.SchemaPath,
                    "--workspace",
                    fixture.WorkspacePath,
                ],
                output,
                error);

            Assert.AreEqual(
                64,
                exitCode,
                $"缺少 {requiredField} 时必须拒绝输入。");
            Assert.AreEqual(string.Empty, output.ToString());
            StringAssert.Contains(error.ToString(), "Schema");
        }
    }

    [TestMethod]
    public async Task Corrupt_workspace_state_returns_conflict_without_absolute_path()
    {
        using var manifestFixture = CliFixture.Create();
        manifestFixture.WriteWorkspaceFile(
            GenerationWorkspaceStore.ManifestRelativePath,
            "{");
        using var manifestOutput = new StringWriter();
        using var manifestError = new StringWriter();

        var manifestExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                manifestFixture.SchemaPath,
                "--workspace",
                manifestFixture.WorkspacePath,
            ],
            manifestOutput,
            manifestError);

        using var artifactFixture = CliFixture.Create();
        artifactFixture.WriteWorkspaceBytes(
            "backend/ProductContracts.g.cs",
            [0xC3, 0x28]);
        using var artifactOutput = new StringWriter();
        using var artifactError = new StringWriter();

        var artifactExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                artifactFixture.SchemaPath,
                "--workspace",
                artifactFixture.WorkspacePath,
            ],
            artifactOutput,
            artifactError);

        using var ownershipFixture = CliFixture.Create();
        ownershipFixture.WriteWorkspaceFile(
            ".fullnet/owned.g.cs",
            "owned");
        ownershipFixture.WriteWorkspaceFile(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
                [
                    new(
                        ".fullnet/owned.g.cs",
                        GenerationContentHash.Compute("owned")),
                ]).ToJson());
        using var ownershipOutput = new StringWriter();
        using var ownershipError = new StringWriter();

        var ownershipExitCode = await CodeGenerationCli.RunAsync(
            [
                "--schema",
                ownershipFixture.SchemaPath,
                "--workspace",
                ownershipFixture.WorkspacePath,
            ],
            ownershipOutput,
            ownershipError);

        Assert.AreEqual(2, manifestExitCode);
        Assert.AreEqual(2, artifactExitCode);
        Assert.AreEqual(2, ownershipExitCode);
        StringAssert.Contains(manifestError.ToString(), "工作区冲突");
        StringAssert.Contains(artifactError.ToString(), "工作区冲突");
        StringAssert.Contains(ownershipError.ToString(), "工作区冲突");
        Assert.IsFalse(
            manifestError.ToString().Contains(
                manifestFixture.RootPath,
                StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(
            artifactError.ToString().Contains(
                artifactFixture.RootPath,
                StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(
            ownershipError.ToString().Contains(
                ownershipFixture.RootPath,
                StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(string.Empty, manifestOutput.ToString());
        Assert.AreEqual(string.Empty, artifactOutput.ToString());
        Assert.AreEqual(string.Empty, ownershipOutput.ToString());
    }

    private const string ValidSchemaJson =
        """
        {
          "ownerKey": "acme",
          "moduleKey": "catalog",
          "entityKey": "product",
          "databaseTableName": "acme_catalog_product",
          "rootNamespace": "Acme.Modules.Catalog",
          "clrTypeName": "Product",
          "apiResourceName": "products",
          "permissionResourceName": "products",
          "isTenantScoped": true,
          "hasVersion": true,
          "columns": [
            {
              "databaseName": "Id",
              "clrPropertyName": "Id",
              "jsonPropertyName": "id",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "TenantId",
              "clrPropertyName": "TenantId",
              "jsonPropertyName": "tenantId",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "Name",
              "clrPropertyName": "Name",
              "jsonPropertyName": "displayName",
              "scalarType": "String",
              "maxLength": 200
            },
            {
              "databaseName": "Description",
              "clrPropertyName": "Description",
              "jsonPropertyName": "description",
              "scalarType": "String",
              "isNullable": true,
              "maxLength": 500
            },
            {
              "databaseName": "IsActive",
              "clrPropertyName": "IsActive",
              "jsonPropertyName": "isActive",
              "scalarType": "Boolean"
            },
            {
              "databaseName": "Version",
              "clrPropertyName": "Version",
              "jsonPropertyName": "version",
              "scalarType": "Int64"
            },
            {
              "databaseName": "CreatedAtUtc",
              "clrPropertyName": "CreatedAtUtc",
              "jsonPropertyName": "createdAtUtc",
              "scalarType": "DateTimeUtc"
            }
          ]
        }
        """;

    private const string ValidBatchMappingJson =
        """
        {
          "tables": [
            {
              "ownerKey": "acme",
              "moduleKey": "catalog",
              "entityKey": "product",
              "rootNamespace": "Acme.Modules.Catalog",
              "clrTypeName": "Product",
              "apiResourceName": "products",
              "permissionResourceName": "products",
              "dataScope": "TenantRequired",
              "hasVersion": true
            }
          ]
        }
        """;

    private const string ValidIntegrationTargetJson =
        """
        {
          "moduleName": "Catalog",
          "moduleProjectPath": "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
          "moduleEntryPointPath": "src/Modules/Acme.Modules.Catalog/CatalogModule.cs",
          "compositionProjectPath": "src/Composition/Acme.Composition/Acme.Composition.csproj",
          "compositionCatalogPath": "src/Composition/Acme.Composition/ModuleCatalog.cs",
          "vueRouterPath": "ui/admin/src/router/index.ts",
          "layuiRouterPath": "ui/admin-layui/js/core/route-controllers.js"
        }
        """;

    private static string[] DatabaseImportArguments(
        string workspacePath,
        string environmentVariable,
        string provider = "sqlserver") =>
        [
            "import-database",
            "--provider",
            provider,
            "--connection-env",
            environmentVariable,
            "--owner-key",
            "acme",
            "--module-key",
            "catalog",
            "--entity-key",
            "product",
            "--root-namespace",
            "Acme.Modules.Catalog",
            "--clr-type",
            "Product",
            "--api-resource",
            "products",
            "--permission-resource",
            "products",
            "--tenant-scoped",
            "true",
            "--has-version",
            "true",
            "--workspace",
            workspacePath,
        ];

    private static string[] DatabaseBatchArguments(
        string workspacePath,
        string mappingPath,
        string environmentVariable,
        string provider = "sqlserver",
        string command = "preview-database-batch") =>
        [
            command,
            "--provider",
            provider,
            "--connection-env",
            environmentVariable,
            "--mapping",
            mappingPath,
            "--workspace",
            workspacePath,
        ];

    private static string[] ModuleIntegrationArguments(
        string schemaPath,
        string repositoryPath,
        string targetPath,
        string command = "plan-module-integration") =>
        [
            command,
            "--schema",
            schemaPath,
            "--repository",
            repositoryPath,
            "--target",
            targetPath,
        ];

    private static void WriteRepositoryFile(
        string repositoryPath,
        string relativePath,
        string content)
    {
        var path = Path.Combine(
            repositoryPath,
            relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(false, true));
    }

    private static Dictionary<string, string> CaptureRepository(
        string repositoryPath) =>
        Directory
            .GetFiles(
                repositoryPath,
                "*",
                SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(repositoryPath, path),
                path => Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private static string ExplicitScopeSchemaJson(string dataScope)
    {
        var root = JsonNode.Parse(ValidSchemaJson)!.AsObject();
        root.Remove("isTenantScoped");
        root["dataScope"] = dataScope;
        var columns = root["columns"]!.AsArray();
        var tenantColumn = columns.Single(column =>
            column!["databaseName"]!.GetValue<string>() == "TenantId");
        columns.Remove(tenantColumn);
        return root.ToJsonString(new()
        {
            WriteIndented = true,
        });
    }

    private static void AddExplicitCapabilities(JsonObject target)
    {
        target["entityCapabilities"] = new JsonObject
        {
            ["deleteMode"] = "soft.delete",
            ["hasCreatedAudit"] = true,
            ["hasUpdatedAudit"] = true,
            ["hasDeletedAudit"] = true,
            ["hasVersion"] = true,
            ["ownershipMode"] = "organization.unit",
        };
    }

    private static void AddHardDeleteCapabilities(JsonObject target)
    {
        target["entityCapabilities"] = new JsonObject
        {
            ["deleteMode"] = "hard.delete",
            ["hasCreatedAudit"] = true,
            ["hasUpdatedAudit"] = false,
            ["hasDeletedAudit"] = false,
            ["hasVersion"] = true,
            ["ownershipMode"] = "none",
        };
        target["columns"]!.AsArray().Add(new JsonObject
        {
            ["databaseName"] = "CreatedById",
            ["clrPropertyName"] = "CreatedById",
            ["jsonPropertyName"] = "createdById",
            ["scalarType"] = "uuid",
        });
    }

    private static JsonObject RelationshipJson(string dependentDataScope) =>
        new()
        {
            ["principalEntityKey"] = "product",
            ["principalColumnName"] = "Id",
            ["principalDataScope"] = "tenant.required",
            ["dependentEntityKey"] = "product_item",
            ["dependentColumnName"] = "ProductId",
            ["dependentDataScope"] = dependentDataScope,
        };

    private static void AddCapabilityColumns(JsonArray columns)
    {
        columns.Add(new JsonObject
        {
            ["databaseName"] = "CreatedById",
            ["clrPropertyName"] = "CreatedById",
            ["jsonPropertyName"] = "createdById",
            ["scalarType"] = "Uuid",
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "UpdatedAtUtc",
            ["clrPropertyName"] = "UpdatedAtUtc",
            ["jsonPropertyName"] = "updatedAtUtc",
            ["scalarType"] = "DateTimeUtc",
            ["isNullable"] = true,
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "UpdatedById",
            ["clrPropertyName"] = "UpdatedById",
            ["jsonPropertyName"] = "updatedById",
            ["scalarType"] = "Uuid",
            ["isNullable"] = true,
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "IsDeleted",
            ["clrPropertyName"] = "IsDeleted",
            ["jsonPropertyName"] = "isDeleted",
            ["scalarType"] = "Boolean",
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "DeletedAtUtc",
            ["clrPropertyName"] = "DeletedAtUtc",
            ["jsonPropertyName"] = "deletedAtUtc",
            ["scalarType"] = "DateTimeUtc",
            ["isNullable"] = true,
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "DeletedById",
            ["clrPropertyName"] = "DeletedById",
            ["jsonPropertyName"] = "deletedById",
            ["scalarType"] = "Uuid",
            ["isNullable"] = true,
        });
        columns.Add(new JsonObject
        {
            ["databaseName"] = "OrganizationUnitId",
            ["clrPropertyName"] = "OrganizationUnitId",
            ["jsonPropertyName"] = "organizationUnitId",
            ["scalarType"] = "Uuid",
        });
    }

    private sealed class CliFixture : IDisposable
    {
        private CliFixture(
            string rootPath,
            string schemaPath,
            string workspacePath)
        {
            RootPath = rootPath;
            SchemaPath = schemaPath;
            WorkspacePath = workspacePath;
        }

        public string RootPath { get; }

        public string SchemaPath { get; }

        public string WorkspacePath { get; }

        public static CliFixture Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"fullnet-codegen-cli-{Guid.NewGuid():N}");
            var workspacePath = Path.Combine(rootPath, "workspace");
            var schemaPath = Path.Combine(rootPath, "schema.json");
            Directory.CreateDirectory(workspacePath);
            File.WriteAllText(
                schemaPath,
                ValidSchemaJson,
                new UTF8Encoding(false, true));
            return new CliFixture(
                rootPath,
                schemaPath,
                workspacePath);
        }

        public string ReadWorkspaceFile(string relativePath)
        {
            return File.ReadAllText(
                WorkspaceFile(relativePath),
                new UTF8Encoding(false, true));
        }

        public void WriteWorkspaceFile(
            string relativePath,
            string content)
        {
            var path = WorkspaceFile(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                content,
                new UTF8Encoding(false, true));
        }

        public void WriteWorkspaceBytes(
            string relativePath,
            byte[] bytes)
        {
            var path = WorkspaceFile(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private string WorkspaceFile(string relativePath)
        {
            return Path.Combine(
                WorkspacePath,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }
    }
}
