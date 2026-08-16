using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.CodeGeneration;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class CodeGenerationApiMySqlTests
{
    [TestMethod]
    public async Task Host_preview_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await CodeGenerationPreviewAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_templates_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await CodeGenerationTemplateAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_catalog_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await CodeGenerationCatalogAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_runs_follow_contract_with_mysql()
    {
        using var workspace = CodeGenerationApplyTestWorkspace.Create();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            workspace.Settings);

        await CodeGenerationRunAssertions.VerifyAsync(
            factory,
            workspace.RootPath);
    }
}
