using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingDataSourceFieldValidatorTests
{
    [TestMethod]
    public void ValidateMetadata_Rejects_ConnectionStringDelimiter_InHost()
    {
        var message = ReportingDataSourceFieldValidator.ValidateMetadata(
            "Sales",
            "sql_server",
            "db;Drop",
            1433,
            "warehouse",
            "reader");

        Assert.IsNotNull(message);
        StringAssert.Contains(message, "Server host");
    }

    [TestMethod]
    public void ValidateMetadata_Accepts_SupportedProvider()
    {
        var message = ReportingDataSourceFieldValidator.ValidateMetadata(
            "Sales",
            "mysql",
            "db.example.com",
            3306,
            "warehouse",
            "reader");

        Assert.IsNull(message);
    }
}
