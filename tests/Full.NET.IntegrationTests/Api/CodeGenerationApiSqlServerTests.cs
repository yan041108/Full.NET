using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.CodeGeneration;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class CodeGenerationApiSqlServerTests
{
    [TestMethod]
    public async Task Host_preview_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await CodeGenerationPreviewAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_templates_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await CodeGenerationTemplateAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_runs_follow_contract_with_sql_server()
    {
        using var workspace = CodeGenerationApplyTestWorkspace.Create();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            workspace.Settings);

        await CodeGenerationRunAssertions.VerifyAsync(
            factory,
            workspace.RootPath);
    }
}
