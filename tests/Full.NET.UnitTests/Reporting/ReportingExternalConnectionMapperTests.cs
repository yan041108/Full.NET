using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Connectivity;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.UnitTests.Reporting;

/// <summary>报表外部连接映射必须停留在受支持提供程序，并把超时交给数据边界。</summary>
[TestClass]
public sealed class ReportingExternalConnectionMapperTests
{
    /// <summary>SQL Server 与 MySQL 映射到对应 Provider，未知键失败关闭。</summary>
    /// <param name="providerKey">数据源提供程序键。</param>
    /// <param name="expectedProvider">期望的数据边界提供程序；-1 表示应拒绝。</param>
    [TestMethod]
    [DataRow(ReportingDataSourceProviderKeys.SqlServer, (int)DatabaseProvider.SqlServer)]
    [DataRow(ReportingDataSourceProviderKeys.MySql, (int)DatabaseProvider.MySql)]
    [DataRow("oracle", -1)]
    public void Mapper_accepts_only_supported_provider_keys(string providerKey, int expectedProvider)
    {
        var record = new ReportingDataSourceRecord
        {
            ProviderKey = providerKey,
            ServerHost = "db.example.internal",
            Port = 1433,
            DatabaseName = "reporting",
            Username = "reader",
            TrustServerCertificate = true,
        };

        var request = ReportingExternalConnectionMapper.TryCreate(
            record,
            "secret",
            "Full.NET-Reporting-Test");

        if (expectedProvider < 0)
        {
            Assert.IsNull(request);
            return;
        }

        Assert.IsNotNull(request);
        Assert.AreEqual((DatabaseProvider)expectedProvider, request.Provider);
        Assert.AreEqual(ReportingExecutionPolicy.ConnectionTimeoutSeconds, request.ConnectionTimeoutSeconds);
        Assert.AreEqual("secret", request.Password);
        Assert.IsTrue(request.TrustServerCertificate);
    }
}
