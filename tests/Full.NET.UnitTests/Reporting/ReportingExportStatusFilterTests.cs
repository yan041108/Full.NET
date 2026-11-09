using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>未知状态在数据库执行前拒绝，机器键大小写和输入内容不能扩大查询。</summary>
[TestClass]
public sealed class ReportingExportStatusFilterTests
{
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("SUCCEEDED")]
    [DataRow("failed' OR 1=1")]
    public async Task Invalid_status_never_reaches_query_executor(string status)
    {
        var queries = Substitute.For<IQueryExecutor>(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        var service = new ReportingExportTaskQueryService(queries, multiple,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Substitute.For<ICurrentTenant>(), null!, null!);
        var result = await service.ListAsync(1, 20, null, status);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonErrorCodes.RequestInvalid, result.Error!.Code);
        Assert.AreEqual(ErrorType.Validation, result.Error.Type);
        Assert.AreEqual(0, queries.ReceivedCalls().Count()); Assert.AreEqual(0, multiple.ReceivedCalls().Count());
    }
}
