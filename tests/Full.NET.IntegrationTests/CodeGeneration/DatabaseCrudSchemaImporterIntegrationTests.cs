using System.Data.Common;
using Dapper;
using Full.NET.Data.CodeGeneration.Schema;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
public sealed class DatabaseCrudSchemaImporterIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_table_metadata_imports_a_validated_crud_schema()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            CREATE TABLE dbo.acme_catalog_product
            (
                Id uniqueidentifier NOT NULL,
                TenantId uniqueidentifier NOT NULL,
                Name nvarchar(200) NOT NULL,
                Description nvarchar(500) NULL,
                Price decimal(18, 2) NOT NULL,
                IsActive bit NOT NULL,
                Version bigint NOT NULL,
                CreatedAtUtc datetimeoffset(7) NOT NULL,
                CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
            );
            """);

        await AssertImportedSchemaAsync(
            connection,
            DatabaseMetadataProvider.SqlServer);
    }

    [TestMethod]
    public async Task MySql_table_metadata_imports_a_validated_crud_schema()
    {
        var connectionString =
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            CREATE TABLE acme_catalog_product
            (
                Id binary(16) NOT NULL,
                TenantId binary(16) NOT NULL,
                Name varchar(200) NOT NULL,
                Description varchar(500) NULL,
                Price decimal(18, 2) NOT NULL,
                IsActive boolean NOT NULL,
                Version bigint NOT NULL,
                CreatedAtUtc datetime(6) NOT NULL,
                CONSTRAINT PK_acme_catalog_product PRIMARY KEY (Id)
            ) ENGINE=InnoDB;
            """);

        await AssertImportedSchemaAsync(
            connection,
            DatabaseMetadataProvider.MySql);
    }

    private static async Task AssertImportedSchemaAsync(
        DbConnection connection,
        DatabaseMetadataProvider provider)
    {
        var schema = await DatabaseCrudSchemaImporter.ImportAsync(
            connection,
            provider,
            new DatabaseCrudImportOptions(
                OwnerKey: "acme",
                ModuleKey: "catalog",
                EntityKey: "product",
                RootNamespace: "Acme.Modules.Catalog",
                ClrTypeName: "Product",
                ApiResourceName: "products",
                PermissionResourceName: "products",
                IsTenantScoped: true,
                HasVersion: true));

        Assert.AreEqual("acme_catalog_product", schema.DatabaseTableName);
        Assert.AreEqual("catalog.products.read", schema.ReadPermission);
        CollectionAssert.AreEqual(
            new[]
            {
                "Id",
                "TenantId",
                "Name",
                "Description",
                "Price",
                "IsActive",
                "Version",
                "CreatedAtUtc",
            },
            schema.Columns.Select(column => column.DatabaseName).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                FullNetScalarType.Uuid,
                FullNetScalarType.Uuid,
                FullNetScalarType.String,
                FullNetScalarType.String,
                FullNetScalarType.Decimal,
                FullNetScalarType.Boolean,
                FullNetScalarType.Int64,
                FullNetScalarType.DateTimeUtc,
            },
            schema.Columns.Select(column => column.ScalarType).ToArray());
        Assert.IsTrue(schema.Columns[3].IsNullable);
        Assert.AreEqual(500, schema.Columns[3].MaxLength);
        Assert.AreEqual(18, schema.Columns[4].NumericPrecision);
        Assert.AreEqual(2, schema.Columns[4].NumericScale);
    }
}
