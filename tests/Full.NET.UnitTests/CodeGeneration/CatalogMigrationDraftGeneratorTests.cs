using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>
/// 校验目录迁移草案生成器在双库输出与跨方言警告上的行为。
/// </summary>
[TestClass]
public sealed class CatalogMigrationDraftGeneratorTests
{
    [TestMethod]
    public void SqlServer_source_uses_native_column_types_in_sql_server_draft()
    {
        var columns = new[]
        {
            new DatabaseColumnMetadata(
                "Id",
                "uniqueidentifier",
                "uniqueidentifier",
                false,
                null,
                1),
            new DatabaseColumnMetadata(
                "Name",
                "nvarchar",
                "nvarchar(128)",
                false,
                256,
                2),
        };

        var draft = CatalogMigrationDraftGenerator.Generate(
            "fn_demo_sample",
            DatabaseMetadataProvider.SqlServer,
            columns);

        StringAssert.Contains(draft.SqlServerDraft, "nvarchar(128)");
        StringAssert.Contains(draft.SqlServerDraft, "PRIMARY KEY CLUSTERED (Id)");
        StringAssert.Contains(draft.MySqlDraft, "varchar(256)");
        Assert.IsTrue(draft.Warnings.Count > 0);
    }

    [TestMethod]
    public void MySql_source_uses_native_column_types_in_mysql_draft()
    {
        var columns = new[]
        {
            new DatabaseColumnMetadata(
                "Id",
                "binary",
                "binary(16)",
                false,
                16,
                1),
            new DatabaseColumnMetadata(
                "IsActive",
                "tinyint",
                "tinyint(1)",
                false,
                null,
                2),
        };

        var draft = CatalogMigrationDraftGenerator.Generate(
            "fn_demo_sample",
            DatabaseMetadataProvider.MySql,
            columns);

        StringAssert.Contains(draft.MySqlDraft, "tinyint(1)");
        StringAssert.Contains(draft.SqlServerDraft, "bit");
        Assert.IsTrue(draft.Warnings.Count > 0);
    }
}
