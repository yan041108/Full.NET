using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>验证企业停用时导入回执的真实文件列表和 Worker 恢复。</summary>
[TestClass]
public sealed class ImportReceiptTenantActivityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Suspended_tenant_preserves_uploaded_receipt_until_reactivation(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await ImportExportWorkerAssertions.VerifyReceiptTenantRecoveryAsync(provider, connection);
    }
}
