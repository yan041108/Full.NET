using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>非法状态在数据库及文档授权查询前拒绝。</summary>
[TestClass]
public sealed class DocumentPreviewStatusFilterTests
{
    [TestMethod]
    [DataRow("unknown")]
    [DataRow("SUCCEEDED")]
    [DataRow("failed' OR 1=1")]
    public async Task Invalid_status_never_reaches_database(string status)
    {
        var queries = Substitute.For<IQueryExecutor>(); var multiple = Substitute.For<IMultiResultQueryExecutor>();
        var service = new HostDocumentPreviewTaskQueryService(queries, multiple, null!, null!, null!, null!);
        var result = await service.ListAsync(1, 20, null, status);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonErrorCodes.RequestInvalid, result.Error!.Code);
        Assert.AreEqual(ErrorType.Validation, result.Error.Type);
        Assert.AreEqual(0, queries.ReceivedCalls().Count()); Assert.AreEqual(0, multiple.ReceivedCalls().Count());
    }
}
