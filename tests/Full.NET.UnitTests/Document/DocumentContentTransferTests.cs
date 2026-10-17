using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Features.ManageHostDocumentShares;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>真实查询服务在外部文件读取后的撤销、取消与日志失败边界。</summary>
[TestClass]
public sealed class DocumentContentTransferTests
{
    [TestMethod]
    [DataRow(false, "disabled")]
    [DataRow(true, "disabled")]
    [DataRow(false, "expired")]
    [DataRow(true, "expired")]
    [DataRow(false, "password")]
    [DataRow(true, "password")]
    [DataRow(false, "oldPassword")]
    [DataRow(true, "oldPassword")]
    [DataRow(false, "documentRemoved")]
    [DataRow(true, "documentRemoved")]
    [DataRow(false, "rebound")]
    [DataRow(true, "rebound")]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    [DataRow(false, "failure")]
    [DataRow(true, "failure")]
    [DataRow(false, "allowed")]
    [DataRow(true, "allowed")]
    public async Task Anonymous_content_rechecks_share_after_open_and_disposes_denied_stream(bool preview, string change)
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var shareId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var current = new DocumentShareRecord { Id = shareId, DocumentId = f.ItemId, IsEnabled = true, ExpireTime = now.AddHours(1), AccessCount = 1,
            PasswordHash = change == "oldPassword" ? "old-hash" : null };
        var hasher = Substitute.For<IDocumentSharePasswordHasher>();
        hasher.Verify(shareId, "old-hash", "old-password").Returns(true);
        var request = new AccessHostDocumentShareRequest(change == "oldPassword" ? "old-password" : null);
        f.Clock.UtcNow.Returns(now);
        f.Queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(DocumentShareSql.FindByCode, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<DocumentShareRecord?>(current));
        f.Opened = () =>
        {
            if (change == "cancel") cancellation.Cancel();
            else if (change == "failure") f.Queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(
                DocumentShareSql.FindByCode, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<DocumentShareRecord?>(new IOException("database unavailable")));
            else if (change == "missing") current = null!;
            else if (change == "documentRemoved") f.Queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(
                DocumentItemSql.FindActiveById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DocumentItemDetailRecord?>(null));
            else if (change != "allowed") current = new DocumentShareRecord
            {
                Id = shareId, DocumentId = change == "rebound" ? Guid.NewGuid() : f.ItemId,
                IsEnabled = change != "disabled", AccessCount = 1,
                ExpireTime = change == "expired" ? now.AddMinutes(-1) : now.AddHours(1),
                PasswordHash = change is "password" or "oldPassword" ? "new-hash" : null,
            };
        };
        var service = new HostDocumentShareManagementService(f.Queries, f.Commands, f.Transaction, null!, f.Items(), null!,
            new HostDocumentPreviewTaskQueryService(f.Queries, Substitute.For<IMultiResultQueryExecutor>(), f.Files, f.Database),
            f.Clock, Substitute.For<IIdGenerator>(), hasher, f.Recorder());
        Task<Result<HostFileContent>> Open() => preview
            ? service.OpenAnonymousPreviewTaskContentAsync("share", f.TaskId, request, cancellation.Token)
            : service.OpenAnonymousContentAsync("share", request, null, cancellation.Token);
        if (change == "failure") await Assert.ThrowsExactlyAsync<IOException>(Open);
        else if (change == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(Open);
        else
        {
            var result = await Open();
            Assert.AreEqual(change == "allowed", result.IsSuccess);
            if (change == "allowed") { Assert.IsTrue(f.Stream.CanRead); await result.Value!.Content.DisposeAsync(); }
            else Assert.IsTrue(result.Error!.Code.StartsWith("document.", StringComparison.Ordinal));
        }
        Assert.IsFalse(f.Stream.CanRead);
        await f.Files.Received(1).OpenReadyContentAsync(f.FileId, Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Transaction.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Share_preview_log_is_written_only_after_share_revalidation(bool revoke)
    {
        var f = new Fixture("Document");
        var share = new DocumentShareRecord { Id = Guid.NewGuid(), DocumentId = f.ItemId, IsEnabled = true,
            ExpireTime = DateTimeOffset.UtcNow.AddHours(1), AccessCount = 1 };
        f.Queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(DocumentShareSql.FindByCode, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(share);
        f.Transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("log unavailable")));
        if (revoke) f.Opened = () => f.Queries.QuerySingleOrDefaultAsync<DocumentShareRecord>(
            DocumentShareSql.FindByCode, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<DocumentShareRecord?>(null));
        var service = new HostDocumentShareManagementService(f.Queries, f.Commands, f.Transaction, null!, f.Items(), null!, null!,
            f.Clock, Substitute.For<IIdGenerator>(), Substitute.For<IDocumentSharePasswordHasher>(), f.Recorder());
        if (revoke)
        {
            Assert.IsFalse((await service.OpenAnonymousContentAsync("share", new(), null)).IsSuccess);
            Assert.AreEqual(0, f.Transaction.ReceivedCalls().Count());
        }
        else
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => service.OpenAnonymousContentAsync("share", new(), null));
            Assert.AreEqual(1, f.Transaction.ReceivedCalls().Count());
        }
        Assert.IsFalse(f.Stream.CanRead);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Document_log_failure_or_late_cancellation_disposes_open_stream(bool preview, bool cancel)
    {
        var f = new Fixture("Document");
        using var cancellation = new CancellationTokenSource();
        if (cancel) f.Opened = cancellation.Cancel;
        else f.Transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("log unavailable")));
        var observation = new DocumentAccessObservation(null, null, HostDocumentAccessTypeKeys.Preview, HostDocumentAccessSourceKeys.Share);
        Task<Result<HostFileContent>> Open() => preview
            ? f.Items().OpenVersionPreviewAsync(f.ItemId, null, observation, cancellation.Token)
            : f.Items().OpenCurrentVersionContentAsync(f.ItemId, observation, cancellation.Token);
        if (cancel) await Assert.ThrowsExactlyAsync<OperationCanceledException>(Open);
        else await Assert.ThrowsExactlyAsync<IOException>(Open);
        Assert.IsFalse(f.Stream.CanRead);
    }

    private sealed class Fixture
    {
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid FileId { get; } = Guid.NewGuid();
        public Guid TaskId { get; } = Guid.NewGuid();
        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ICommandTransaction Transaction { get; } = Substitute.For<ICommandTransaction>();
        public IHostFileContentReader Files { get; } = Substitute.For<IHostFileContentReader>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public IOptions<DatabaseOptions> Database { get; } = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        public MemoryStream Stream { get; } = new([1]);
        public Action? Opened { get; set; }

        public Fixture(string title = "")
        {
            Queries.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(new DocumentItemDetailRecord { Id = ItemId, FileId = FileId, Title = title });
            Queries.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(new DocumentPreviewTaskRecord { Id = TaskId, DocumentItemId = ItemId, OutputFileId = FileId, StatusKey = HostDocumentPreviewTaskStatusKeys.Succeeded });
            Files.OpenReadyContentAsync(FileId, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Opened?.Invoke();
                return Task.FromResult(Result<HostFileContent>.Success(new(Stream, "application/pdf", "result.pdf")));
            });
        }

        public DocumentAccessLogRecorder Recorder() => new(Commands, Transaction, Clock, Substitute.For<IIdGenerator>());
        public HostDocumentItemQueryService Items() => new(Substitute.For<IMultiResultQueryExecutor>(), Queries, Files, Recorder(), null!, Database);
    }
}
