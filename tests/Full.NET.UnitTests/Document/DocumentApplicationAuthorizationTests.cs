using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Security;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>不经 HTTP 调用应用边界仍必须执行文档 ACL。</summary>
[TestClass]
public sealed class DocumentApplicationAuthorizationTests
{
    [TestMethod]
    public async Task Direct_detail_query_denied_before_loading_document_or_files()
    {
        var policy = new TestDocumentAccessPolicy(new Error(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.AccessDenied, "Denied", ErrorType.Forbidden));
        var queries = Substitute.For<IQueryExecutor>();
        var service = new HostDocumentItemQueryService(null!, queries, null!, null!, null!,
            Options.Create(new DatabaseOptions()), policy);
        var result = await service.GetByIdAsync(Guid.CreateVersion7());
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.AccessDenied, result.Error!.Code);
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Actor_port_without_http_can_authorize_and_missing_actor_fails_closed()
    {
        var actor = new TestDocumentActorContext();
        var queries = Substitute.For<IQueryExecutor>();
        var policy = new DocumentAccessPolicy(queries, actor);
        Assert.IsNotNull(await policy.CheckAsync(Guid.CreateVersion7(), DocumentOperation.Read, CancellationToken.None));
        Assert.AreEqual(0, queries.ReceivedCalls().Count());
        actor.UserId = Guid.CreateVersion7();
        queries.QuerySingleOrDefaultAsync<long>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1L);
        Assert.IsNull(await policy.CheckAsync(Guid.CreateVersion7(), DocumentOperation.Write, CancellationToken.None));
    }

    [TestMethod]
    public async Task Direct_write_denied_before_transaction_or_upload()
    {
        var policy = new TestDocumentAccessPolicy(new Error(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.AccessDenied, "Denied", ErrorType.Forbidden));
        var service = new HostDocumentItemManagementService(null!, null!, null!, null!, null!, null!, null!, null!, null!, policy);
        var result = await service.AddVersionFromUploadAsync(Guid.CreateVersion7(), Guid.CreateVersion7(),
            "sample.txt", "text/plain", Stream.Null, 1);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(Full.NET.Modules.Document.Contracts.DocumentErrorCodes.AccessDenied, result.Error!.Code);
    }
}
