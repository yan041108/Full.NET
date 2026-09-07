using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.ImportExport;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ImportExportApiMySqlTests
{
    [TestMethod]
    public async Task Import_task_preview_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["FullNet:ImportExport:RunSynchronously"] = "true",
            });

        await ImportExportTaskAssertions.VerifyImportTaskPreviewContractAsync(factory);
    }
}
