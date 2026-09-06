using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExecutionParameterBinderTests
{
    [TestMethod]
    public void Bind_applies_default_topN_for_schema_inventory()
    {
        var queryPort = ReportingQueryPortCatalog.List()
            .Single(port => port.QueryPortKey == "reporting.schema_inventory");
        var schema = queryPort.Parameters
            .Select(parameter => new ReportingParameterSchemaEntry(
                parameter.ParameterKey,
                parameter.DisplayName,
                parameter.DataTypeKey,
                parameter.IsRequired,
                parameter.DefaultValue))
            .ToArray();

        var outcome = ReportingExecutionParameterBinder.Bind(queryPort, schema, []);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual(20, outcome.Values["topN"]);
    }

    [TestMethod]
    public void Bind_accepts_topN_within_port_maximum()
    {
        var queryPort = ReportingQueryPortCatalog.List()
            .Single(port => port.QueryPortKey == "reporting.schema_inventory");
        var schema = queryPort.Parameters
            .Select(parameter => new ReportingParameterSchemaEntry(
                parameter.ParameterKey,
                parameter.DisplayName,
                parameter.DataTypeKey,
                parameter.IsRequired,
                parameter.DefaultValue))
            .ToArray();

        var outcome = ReportingExecutionParameterBinder.Bind(
            queryPort,
            schema,
            [new ReportingExecutionParameterValue("topN", "150")]);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual(150, outcome.Values["topN"]);
    }
}
