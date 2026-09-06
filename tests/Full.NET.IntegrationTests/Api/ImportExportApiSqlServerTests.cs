using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.ImportExport;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ImportExportApiSqlServerTests
{
    [TestMethod]
    public async Task Import_task_preview_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["FullNet:ImportExport:RunSynchronously"] = "true",
            });

        await ImportExportTaskAssertions.VerifyImportTaskPreviewContractAsync(factory);
    }
}
