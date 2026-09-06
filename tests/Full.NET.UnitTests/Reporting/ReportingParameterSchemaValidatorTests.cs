using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingParameterSchemaValidatorTests
{
    [TestMethod]
    public void Validate_accepts_schema_matching_query_port()
    {
        var queryPort = CreateSchemaInventoryPort();
        var schema = new[]
        {
            new ReportingParameterSchemaEntry("topN", "返回条数", ReportingParameterDataTypeKeys.Integer, true, "20"),
        };

        Assert.IsNull(ReportingParameterSchemaValidator.Validate(queryPort, schema));
    }

    [TestMethod]
    public void Validate_rejects_missing_parameter()
    {
        var queryPort = CreateSchemaInventoryPort();

        var error = ReportingParameterSchemaValidator.Validate(queryPort, []);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "topN");
    }

    [TestMethod]
    public void Validate_rejects_extra_parameter()
    {
        var queryPort = CreateSchemaInventoryPort();
        var schema = new[]
        {
            new ReportingParameterSchemaEntry("topN", "返回条数", ReportingParameterDataTypeKeys.Integer, true, "20"),
            new ReportingParameterSchemaEntry("extra", "多余", ReportingParameterDataTypeKeys.String, false, null),
        };

        var error = ReportingParameterSchemaValidator.Validate(queryPort, schema);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "extra");
    }

    private static ReportingQueryPortDefinition CreateSchemaInventoryPort() =>
        new(
            "reporting.schema_inventory",
            "Schema 清单",
            "测试",
            [ReportingDataSourceProviderKeys.SqlServer],
            [
                new ReportingQueryPortParameterDefinition(
                    "topN",
                    "返回条数上限",
                    ReportingParameterDataTypeKeys.Integer,
                    true,
                    "20",
                    1,
                    200),
            ]);
}
