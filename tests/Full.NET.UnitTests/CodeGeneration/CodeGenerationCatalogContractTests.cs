using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationCatalogContractTests
{
    [TestMethod]
    public void Default_ui_keeps_system_columns_out_of_write_forms()
    {
        var id = FullNetColumnUi.DefaultFor("Id", FullNetScalarType.Uuid, false);
        var name = FullNetColumnUi.DefaultFor(
            "Name",
            FullNetScalarType.String,
            false);

        Assert.IsFalse(id.IncludeInCreate);
        Assert.IsFalse(id.IncludeInUpdate);
        Assert.AreEqual(FullNetColumnControlKind.Uuid, id.ControlKind);
        Assert.IsTrue(name.IncludeInCreate);
        Assert.IsTrue(name.Required);
        Assert.AreEqual(FullNetColumnControlKind.Text, name.ControlKind);
        Assert.AreEqual(FullNetColumnQueryKind.Contains, name.QueryKind);
    }

    [TestMethod]
    public void Catalog_sql_is_host_only_and_shares_cli_query_text()
    {
        Assert.AreEqual(
            SqlDataScope.HostOnly,
            CodeGenerationCatalogSql.ListTablesSqlServer.Scope);
        Assert.AreEqual(
            SqlDataScope.HostOnly,
            CodeGenerationCatalogSql.ListTablesMySql.Scope);
        Assert.AreEqual(
            SqlDataScope.HostOnly,
            CodeGenerationCatalogSql.ListColumnsSqlServer.Scope);
        Assert.AreEqual(
            SqlDataScope.HostOnly,
            CodeGenerationCatalogSql.ListColumnsMySql.Scope);
        Assert.AreEqual(
            DatabaseCatalogQueries.ListTablesSqlServer,
            CodeGenerationCatalogSql.ListTablesSqlServer.Text);
        Assert.IsFalse(DatabaseCatalogQueries.IsSafeTableName("../etc"));
        Assert.IsTrue(
            DatabaseCatalogQueries.IsSafeTableName("fn_codegeneration_template"));
    }
}
