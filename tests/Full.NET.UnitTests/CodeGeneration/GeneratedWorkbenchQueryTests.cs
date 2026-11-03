using Acme.Remediation.Generated;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>调用实际生成的查询契约，未知排序与超精度线值在数据库调用前拒绝。</summary>
[TestClass]
public sealed class GeneratedWorkbenchQueryTests
{
    [TestMethod]
    public void Schema_rejects_incompatible_controls_and_system_write_flags_and_reserves_import_export()
    {
        var original = Full.NET.Tests.CodeGeneration.CodeGenerationRemediationSchemas.Workbench();
        Full.NET.Data.CodeGeneration.Schema.FullNetCrudSchema Copy(IEnumerable<Full.NET.Data.CodeGeneration.Schema.FullNetColumn> columns) =>
            Full.NET.Data.CodeGeneration.Schema.FullNetCrudSchema.CreateProject(original.OwnerKey, original.ModuleKey, original.EntityKey,
                original.DatabaseTableName, original.RootNamespace, original.ClrTypeName, original.ApiResourceName, original.PermissionResourceName,
                original.DataScope, original.EntityCapabilities, columns.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => Copy(original.Columns.Select(column => column.DatabaseName == "Name"
            ? column with { Ui = column.ResolvedUi with { ControlKind = Full.NET.Data.CodeGeneration.Schema.FullNetColumnControlKind.Number } } : column)));
        Assert.ThrowsExactly<ArgumentException>(() => Copy(original.Columns.Select(column => column.DatabaseName == "Id"
            ? column with { Ui = column.ResolvedUi with { IncludeInCreate = true } } : column)));
        Assert.IsFalse(original.Columns.Single(column => column.DatabaseName == "Name").ResolvedUi.IncludeInImportExport);
    }

    [TestMethod]
    [DataRow("Name DESC; DROP TABLE acme_catalog_workbench")]
    [DataRow("version")]
    [DataRow("unknown")]
    public async Task Undeclared_sort_never_reaches_the_database(string sort)
    {
        var multi = Substitute.For<IMultiResultQueryExecutor>();
        var service = new WorkbenchQueryService(Substitute.For<IQueryExecutor>(), multi, Options.Create(new DatabaseOptions()));
        var result = await service.ListAsync(1, 20, query: new() { SortBy = sort });
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("catalog.workbenches.invalid_query", result.Error!.Code);
        Assert.IsFalse(multi.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Reversed_range_is_validation_failure_before_database_work()
    {
        var multi = Substitute.For<IMultiResultQueryExecutor>();
        var service = new WorkbenchQueryService(Substitute.For<IQueryExecutor>(), multi, Options.Create(new DatabaseOptions()));
        var result = await service.ListAsync(1, 20, query: new() { FilterPriceFrom = 2m, FilterPriceTo = 1m });
        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(multi.ReceivedCalls().Any());
    }

    [TestMethod]
    public void Query_wire_values_reject_rounding_and_unknown_case_cannot_bypass_validation()
    {
        Assert.IsFalse(WorkbenchListQuery.IsWireValueValid("filterPriceFrom", "1.000000000000000000000000000001"));
        Assert.IsFalse(WorkbenchListQuery.IsWireValueValid("filterQuantity", "9223372036854775808"));
        Assert.IsFalse(WorkbenchListQuery.IsWireValueValid("filterOccurredAtUtcFrom", "2026-10-26T00:00:00.1234567Z"));
        Assert.IsTrue(WorkbenchListQuery.IsWireValueValid("filterOccurredAtUtcFrom", "2026-10-26T00:00:00.123456Z"));
        Assert.IsFalse(WorkbenchListQuery.QueryNames.Contains("FilterPriceFrom"));
        Assert.IsFalse(WorkbenchListQuery.QueryNames.Contains("tenantId"));
    }
}
