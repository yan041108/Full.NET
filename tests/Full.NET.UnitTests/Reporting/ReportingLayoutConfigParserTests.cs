using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingLayoutConfigParserTests
{
    [TestMethod]
    public void ResolveVisibleColumns_hides_columns_without_permission()
    {
        const string layout = """
            {
              "columns": [
                { "key": "SchemaName", "displayName": "Schema", "requiredPermission": "reporting.executions.columns.schema_name" },
                { "key": "EngineVersion", "displayName": "Version", "requiredPermission": "reporting.executions.columns.engine_version" }
              ]
            }
            """;

        var layoutColumns = ReportingLayoutConfigParser.ParseColumns(layout);
        var visible = ReportingLayoutConfigParser.ResolveVisibleColumns(
            layoutColumns,
            ["SchemaName", "EngineVersion"],
            permission => permission == ReportingExecutionPermissions.ColumnSchemaName);

        CollectionAssert.AreEquivalent(
            new[] { "SchemaName" },
            visible.Select(column => column.ColumnKey).ToArray());
    }
}
