using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExecutionSqlBuilderTests
{
    [TestMethod]
    [DataRow(ReportingDataSourceProviderKeys.SqlServer, 2147483647, 200)]
    [DataRow(ReportingDataSourceProviderKeys.MySql, 2147483647, 200)]
    [DataRow(ReportingDataSourceProviderKeys.SqlServer, 429496731, 10)]
    [DataRow(ReportingDataSourceProviderKeys.MySql, 429496731, 10)]
    public void Build_keeps_large_offsets_positive_without_wrapping_into_earlier_pages(string provider, int page, int size)
    {
        var (_, parameters) = ReportingExecutionSqlBuilder.Build("reporting.schema_inventory", provider,
            new Dictionary<string, object?> { ["topN"] = 200 }, page, size);
        Assert.AreEqual(((long)page - 1) * size, parameters["Offset"]);
    }

    [TestMethod]
    public void Build_wraps_schema_inventory_with_offset_fetch_for_sql_server()
    {
        var (sql, parameters) = ReportingExecutionSqlBuilder.Build(
            "reporting.schema_inventory",
            ReportingDataSourceProviderKeys.SqlServer,
            new Dictionary<string, object?> { ["topN"] = 20 },
            page: 2,
            pageSize: 10);

        StringAssert.Contains(sql, "OFFSET @Offset");
        StringAssert.Contains(sql, "FETCH NEXT @PageSize");
        Assert.AreEqual(10L, parameters["Offset"]);
        Assert.AreEqual(10, parameters["PageSize"]);
    }
}
