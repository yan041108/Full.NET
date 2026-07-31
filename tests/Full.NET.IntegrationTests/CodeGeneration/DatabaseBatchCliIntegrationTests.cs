extern alias codegencli;

using System.Text;
using Dapper;
using Full.NET.Data.CodeGeneration.Generation;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using CodeGenerationCli =
    codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
public sealed class DatabaseBatchCliIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_batch_preview_then_apply_is_idempotent()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(SqlServerTablesSql);
        }

        await AssertBatchPreviewAndApplyAsync("sqlserver", connectionString);
    }

    [TestMethod]
    public async Task MySql_batch_preview_then_apply_is_idempotent()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(MySqlTablesSql);
        }

        await AssertBatchPreviewAndApplyAsync("mysql", connectionString);
    }

    private static async Task AssertBatchPreviewAndApplyAsync(
        string provider,
        string connectionString)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-batch-preview-{Guid.NewGuid():N}");
        var workspacePath = Path.Combine(rootPath, "workspace");
        var mappingPath = Path.Combine(rootPath, "mapping.json");
        var environmentVariable =
            $"FULLNET_CODEGEN_BATCH_CONNECTION_{Guid.NewGuid():N}";
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(
            mappingPath,
            BatchMappingJson,
            new UTF8Encoding(false, true));
        Environment.SetEnvironmentVariable(
            environmentVariable,
            connectionString);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var previewExitCode = await CodeGenerationCli.RunAsync(
                [
                    "preview-database-batch",
                    "--provider",
                    provider,
                    "--connection-env",
                    environmentVariable,
                    "--mapping",
                    mappingPath,
                    "--workspace",
                    workspacePath,
                ],
                output,
                error);

            Assert.AreEqual(0, previewExitCode, error.ToString());
            var previewActions = output.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(26, previewActions);
            Assert.IsTrue(previewActions.All(line =>
                line.StartsWith("Create ", StringComparison.Ordinal)));
            CollectionAssert.Contains(
                previewActions,
                "Create backend/ProductContracts.g.cs");
            CollectionAssert.Contains(
                previewActions,
                "Create backend/OrderContracts.g.cs");
            Assert.AreEqual(string.Empty, error.ToString());
            Assert.AreEqual(
                0,
                Directory.GetFiles(
                    workspacePath,
                    "*",
                    SearchOption.AllDirectories).Length);

            using var applyOutput = new StringWriter();
            using var applyError = new StringWriter();
            var applyExitCode = await CodeGenerationCli.RunAsync(
                [
                    "apply-database-batch",
                    "--provider",
                    provider,
                    "--connection-env",
                    environmentVariable,
                    "--mapping",
                    mappingPath,
                    "--workspace",
                    workspacePath,
                ],
                applyOutput,
                applyError);

            Assert.AreEqual(0, applyExitCode, applyError.ToString());
            var applyActions = applyOutput.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(26, applyActions);
            Assert.IsTrue(applyActions.All(line =>
                line.StartsWith("Create ", StringComparison.Ordinal)));
            Assert.AreEqual(string.Empty, applyError.ToString());
            var manifest = GenerationManifest.Parse(File.ReadAllText(
                Path.Combine(
                    workspacePath,
                    GenerationWorkspaceStore.ManifestRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)),
                new UTF8Encoding(false, true)));
            Assert.HasCount(26, manifest.Artifacts);
            Assert.AreEqual(
                27,
                Directory.GetFiles(
                    workspacePath,
                    "*",
                    SearchOption.AllDirectories).Length);

            using var repeatOutput = new StringWriter();
            using var repeatError = new StringWriter();
            var repeatExitCode = await CodeGenerationCli.RunAsync(
                [
                    "apply-database-batch",
                    "--provider",
                    provider,
                    "--connection-env",
                    environmentVariable,
                    "--mapping",
                    mappingPath,
                    "--workspace",
                    workspacePath,
                ],
                repeatOutput,
                repeatError);

            Assert.AreEqual(0, repeatExitCode, repeatError.ToString());
            Assert.AreEqual(
                26,
                repeatOutput.ToString().Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries).Count(line =>
                    line.StartsWith(
                        "Unchanged ",
                        StringComparison.Ordinal)));
            Assert.AreEqual(string.Empty, repeatError.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                null);
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private const string BatchMappingJson =
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
            },
            {
              "ownerKey": "acme",
              "moduleKey": "sales",
              "entityKey": "order",
              "rootNamespace": "Acme.Modules.Sales",
              "clrTypeName": "Order",
              "apiResourceName": "orders",
              "permissionResourceName": "orders",
              "dataScope": "TenantRequired",
              "hasVersion": true
            }
          ]
        }
        """;

    private const string SqlServerTablesSql =
        """
        CREATE TABLE dbo.acme_catalog_product
        (
            Id uniqueidentifier NOT NULL,
            TenantId uniqueidentifier NOT NULL,
            Name nvarchar(200) NOT NULL,
            IsActive bit NOT NULL,
            Version bigint NOT NULL,
            CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
        );
        CREATE TABLE dbo.acme_sales_order
        (
            Id uniqueidentifier NOT NULL,
            TenantId uniqueidentifier NOT NULL,
            Name nvarchar(200) NOT NULL,
            IsActive bit NOT NULL,
            Version bigint NOT NULL,
            CONSTRAINT PK_acme_sales_order PRIMARY KEY (Id)
        );
        """;

    private const string MySqlTablesSql =
        """
        CREATE TABLE acme_catalog_product
        (
            Id binary(16) NOT NULL,
            TenantId binary(16) NOT NULL,
            Name varchar(200) NOT NULL,
            IsActive boolean NOT NULL,
            Version bigint NOT NULL,
            CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
        ) ENGINE=InnoDB;
        CREATE TABLE acme_sales_order
        (
            Id binary(16) NOT NULL,
            TenantId binary(16) NOT NULL,
            Name varchar(200) NOT NULL,
            IsActive boolean NOT NULL,
            Version bigint NOT NULL,
            CONSTRAINT PK_acme_sales_order PRIMARY KEY (Id)
        ) ENGINE=InnoDB;
        """;
}
