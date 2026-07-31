extern alias codegencli;

using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using CodeGenerationCli =
    codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
public sealed class DatabaseImportCliIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_database_import_cli_previews_without_writing()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(SqlServerTableSql);
        }

        await AssertPreviewAsync("sqlserver", connectionString);
    }

    [TestMethod]
    public async Task MySql_database_import_cli_previews_without_writing()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(MySqlTableSql);
        }

        await AssertPreviewAsync("mysql", connectionString);
    }

    private static async Task AssertPreviewAsync(
        string provider,
        string connectionString)
    {
        var workspacePath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-import-cli-{Guid.NewGuid():N}");
        var environmentVariable =
            $"FULLNET_CODEGEN_CONNECTION_{Guid.NewGuid():N}";
        Directory.CreateDirectory(workspacePath);
        Environment.SetEnvironmentVariable(
            environmentVariable,
            connectionString);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
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
                ],
                output,
                error);

            Assert.AreEqual(0, exitCode, error.ToString());
            StringAssert.Contains(
                output.ToString(),
                "Create backend/ProductContracts.g.cs");
            Assert.AreEqual(string.Empty, error.ToString());
            Assert.AreEqual(
                0,
                Directory.GetFiles(
                    workspacePath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                null);
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private const string SqlServerTableSql =
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
        """;

    private const string MySqlTableSql =
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
        """;
}
