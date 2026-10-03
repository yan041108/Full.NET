using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Tests.Shared.CodeGeneration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>在无数据库内循环中预检实际恢复夹具的双库草案输入。</summary>
[TestClass]
public sealed class GeneratedMigrationRecoveryFixtureTests
{
    [TestMethod]
    [DataRow("SqlServer")]
    [DataRow("MySql")]
    public void Recovery_fixture_generates_valid_provider_draft(string provider)
    {
        var schema = GeneratedMigrationRecoveryFixture.CreateSchema();
        var draft = CrudArtifactGenerator.Generate(schema).Single(artifact => artifact.RelativePath
            == $"templates/migrations/{provider}/CreateProduct.sql.template").Content;
        StringAssert.Contains(draft, provider == "SqlServer"
            ? "CREATE TABLE dbo.acme_catalog_product"
            : "CREATE TABLE IF NOT EXISTS acme_catalog_product");
        StringAssert.Contains(draft, "IX_acme_catalog_product_TenantId_Id");
        StringAssert.Contains(draft, provider == "SqlServer" ? "IsActive bit NOT NULL" : "IsActive boolean NOT NULL");
    }
}
