using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExecutionSqlBuilderTests
{
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
        Assert.AreEqual(10, parameters["Offset"]);
        Assert.AreEqual(10, parameters["PageSize"]);
    }
}
