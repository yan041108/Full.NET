extern alias codegencli;

using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using CodeGenerationCli =
    codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
public sealed class DatabaseTableCatalogCliIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_catalog_lists_base_tables_in_ordinal_order()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(SqlServerTablesSql);
            await connection.ExecuteAsync(SqlServerViewSql);
        }

        await AssertCatalogAsync("sqlserver", connectionString);
    }

    [TestMethod]
    public async Task MySql_catalog_lists_base_tables_in_ordinal_order()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(MySqlCatalogSql);
        }

        await AssertCatalogAsync("mysql", connectionString);
    }

    private static async Task AssertCatalogAsync(
        string provider,
        string connectionString)
    {
        var environmentVariable =
            $"FULLNET_CODEGEN_CATALOG_CONNECTION_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(
            environmentVariable,
            connectionString);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                [
                    "list-database-tables",
                    "--provider",
                    provider,
                    "--connection-env",
                    environmentVariable,
                ],
                output,
                error);

            Assert.AreEqual(0, exitCode, error.ToString());
            CollectionAssert.AreEqual(
                new[]
                {
                    "Table acme_catalog_product",
                    "Table acme_sales_order",
                },
                output.ToString().Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries));
            Assert.AreEqual(string.Empty, error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                null);
        }
    }

    private const string SqlServerTablesSql =
        """
        CREATE TABLE dbo.acme_sales_order
        (
            Id uniqueidentifier NOT NULL,
            CONSTRAINT PK_acme_sales_order PRIMARY KEY (Id)
        );
        CREATE TABLE dbo.acme_catalog_product
        (
            Id uniqueidentifier NOT NULL,
            CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
        );
        """;

    private const string SqlServerViewSql =
        """
        CREATE VIEW dbo.acme_catalog_product_view
        AS SELECT Id FROM dbo.acme_catalog_product;
        """;

    private const string MySqlCatalogSql =
        """
        CREATE TABLE acme_sales_order
        (
            Id binary(16) NOT NULL,
            CONSTRAINT PK_acme_sales_order PRIMARY KEY (Id)
        ) ENGINE=InnoDB;
        CREATE TABLE acme_catalog_product
        (
            Id binary(16) NOT NULL,
            CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
        ) ENGINE=InnoDB;
        CREATE VIEW acme_catalog_product_view
        AS SELECT Id FROM acme_catalog_product;
        """;
}
