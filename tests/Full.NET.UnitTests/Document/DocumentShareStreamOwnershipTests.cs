using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Features.ManageHostDocumentShares;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Files.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>文件流已打开后发生权威查询故障时，不得把资源遗留给无法取得流的调用方。</summary>
[TestClass]
public sealed class DocumentShareStreamOwnershipTests
{
    [TestMethod]
    public async Task Revalidation_exception_disposes_already_opened_content()
    {
        var now = DateTimeOffset.UtcNow;
        var shareId = Guid.CreateVersion7(); var documentId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7();
        var session = new DocumentShareSessionRecord { Id = Guid.CreateVersion7(), ShareId = shareId, DocumentId = documentId,
            VersionId = versionId, AccessRevision = 1, PolicyRevision = 1, ExpiresAtUtc = now.AddMinutes(10) };
        var queries = Substitute.For<IQueryExecutor>(); var reads = 0;
        queries.QuerySingleOrDefaultAsync<DocumentShareSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++reads == 1 ? Task.FromResult<DocumentShareSessionRecord?>(session)
                : Task.FromException<DocumentShareSessionRecord?>(new InvalidOperationException("Authoritative read failed.")));
        queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentShareRecord { Id = shareId, IsEnabled = true, PolicyRevision = 1, ExpireTime = now.AddHours(1) });
        queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentItemDetailRecord { Id = documentId, Title = "Actual title", VersionId = versionId, Status = 1, AccessRevision = 1 });
        var fileId = Guid.CreateVersion7();
        queries.QuerySingleOrDefaultAsync<DocumentVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentVersionRecord { Id = versionId, FileId = fileId });
        var contentReader = Substitute.For<IHostFileContentReader>();
        using var stream = new MemoryStream([1, 2, 3]);
        contentReader.OpenReadyContentAsync(fileId, Arg.Any<CancellationToken>())
            .Returns(Result<HostFileContent>.Success(new(stream, "text/plain", "source.txt")));
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var protection = new EphemeralDataProtectionProvider();
        var credential = protection.CreateProtector("Full.NET.Document.ShareSession.v1").Protect(session.Id.ToString("N"));
        var service = new DocumentShareSessionService(queries, null!, null!, null!, clock, null!, null!, protection,
            contentReader, null!, Options.Create(new DocumentShareSessionOptions()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ReadAsync("share",
            new AccessHostDocumentShareRequest { PreviewGrant = credential }, null, CancellationToken.None));
        Assert.IsFalse(stream.CanRead);
    }
}
