using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingQueryPortCatalogTests
{
    [TestMethod]
    public void List_contains_builtin_ports()
    {
        var ports = ReportingQueryPortCatalog.List();

        Assert.IsTrue(ports.Any(port => port.QueryPortKey == "reporting.database_engine_version"));
        Assert.IsTrue(ports.Any(port => port.QueryPortKey == "reporting.schema_inventory"));
    }

    [TestMethod]
    public void ResolveSql_returns_provider_specific_sql()
    {
        var sqlServerSql = ReportingQueryPortCatalog.ResolveSql(
            "reporting.database_engine_version",
            ReportingDataSourceProviderKeys.SqlServer);
        var mySqlSql = ReportingQueryPortCatalog.ResolveSql(
            "reporting.database_engine_version",
            ReportingDataSourceProviderKeys.MySql);

        Assert.IsNotNull(sqlServerSql);
        Assert.IsNotNull(mySqlSql);
        StringAssert.Contains(sqlServerSql, "SERVERPROPERTY");
        StringAssert.Contains(mySqlSql, "VERSION()");
    }

    [TestMethod]
    public void TryGet_returns_null_for_unknown_port()
    {
        Assert.IsNull(ReportingQueryPortCatalog.TryGet("reporting.unknown"));
    }
}
