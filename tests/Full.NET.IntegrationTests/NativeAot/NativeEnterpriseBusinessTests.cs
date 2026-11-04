using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>同场景验证双库上的原生 API、Worker 与企业申请业务，不用进程内服务代替投递。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeEnterpriseBusinessTests
{
    [TestMethod]
    public async Task MySql_native_hosts_complete_enterprise_approval_and_workbook_import()
    {
        NativeEnterpriseBusinessAssertions.RequireArtifacts();
        await NativeEnterpriseBusinessAssertions.VerifyAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_hosts_complete_enterprise_approval_and_workbook_import()
    {
        NativeEnterpriseBusinessAssertions.RequireArtifacts();
        await NativeEnterpriseBusinessAssertions.VerifyAsync(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
