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
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>六种真实内容入口在迟到取消时停止交付，并释放已打开但未移交的流。</summary>
[TestClass]
public sealed class DocumentContentHandoffBoundaryTests
{
    [TestMethod]
    [DataRow("current", "entry")]
    [DataRow("preview-current", "entry")]
    [DataRow("preview-version", "entry")]
    [DataRow("task", "entry")]
    [DataRow("share-current", "entry")]
    [DataRow("share-task", "entry")]
    [DataRow("current", "source")]
    [DataRow("preview-current", "source")]
    [DataRow("preview-version", "source")]
    [DataRow("task", "source")]
    [DataRow("share-current", "source")]
    [DataRow("share-task", "source")]
    [DataRow("current", "source-missing")]
    [DataRow("preview-current", "source-missing")]
    [DataRow("preview-version", "source-missing")]
    [DataRow("task", "source-missing")]
    [DataRow("share-current", "source-missing")]
    [DataRow("share-task", "source-missing")]
    [DataRow("current", "file")]
    [DataRow("preview-current", "file")]
    [DataRow("preview-version", "file")]
    [DataRow("task", "file")]
    [DataRow("share-current", "file")]
    [DataRow("share-task", "file")]
    [DataRow("current", "file-failed")]
    [DataRow("preview-current", "file-failed")]
    [DataRow("preview-version", "file-failed")]
    [DataRow("task", "file-failed")]
    [DataRow("share-current", "file-failed")]
    [DataRow("share-task", "file-failed")]
    [DataRow("preview-current", "mime")]
    [DataRow("preview-version", "mime")]
    [DataRow("preview-version", "version")]
    [DataRow("preview-version", "version-missing")]
    [DataRow("share-current", "final-share")]
    [DataRow("share-task", "final-share")]
    [DataRow("share-current", "final-document")]
    [DataRow("share-task", "final-document")]
    [DataRow("share-current", "password")]
    [DataRow("share-task", "password")]
    [DataRow("share-current", "password-denied")]
    [DataRow("share-task", "password-denied")]
    [DataRow("share-current", "share-initial")]
    [DataRow("share-task", "share-initial")]
    [DataRow("share-current", "share-initial-missing")]
    [DataRow("share-task", "share-initial-missing")]
    public async Task Ignored_cancellation_does_not_become_success_missing_resource_or_unsupported_mime(string entry, string stage)
    {
        using var fixture = new Fixture(stage);
        if (stage == "entry") fixture.Cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Open(entry));
        Assert.AreEqual(fixture.HandedOff ? 1 : 0, fixture.Stream.DisposeCalls);
        Assert.AreEqual(stage is "file" or "file-failed" or "mime" or "final-share" or "final-document" ? 1 : 0, fixture.FileCalls);
        if (stage == "entry") Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Transaction.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("current")]
    [DataRow("preview-current")]
    [DataRow("preview-version")]
    [DataRow("task")]
    [DataRow("share-current")]
    [DataRow("share-task")]
    public async Task Allowed_content_transfers_the_original_stream_once(string entry)
    {
        using var fixture = new Fixture("allowed");
        var result = await fixture.Open(entry);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(fixture.Stream, result.Value!.Content);
        Assert.AreEqual(0, fixture.Stream.DisposeCalls);
        await result.Value.Content.DisposeAsync();
        Assert.AreEqual(1, fixture.Stream.DisposeCalls);
    }

    [TestMethod]
    [DataRow("preview-current", false)]
    [DataRow("preview-version", false)]
    [DataRow("current", true)]
    [DataRow("task", true)]
    public async Task Unsupported_preview_mime_is_released_while_download_policy_is_preserved(string entry, bool allowed)
    {
        using var fixture = new Fixture("unsupported");
        var result = await fixture.Open(entry);
        Assert.AreEqual(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.AreEqual(0, fixture.Stream.DisposeCalls);
            await result.Value!.Content.DisposeAsync();
        }
        else Assert.AreEqual(Errors.PreviewNotSupported, result.Error!.Code);
        Assert.AreEqual(1, fixture.Stream.DisposeCalls);
        Assert.AreEqual(0, fixture.Transaction.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("item-list")]
    [DataRow("item-detail")]
    [DataRow("versions")]
    [DataRow("task-list")]
    [DataRow("task-detail")]
    public async Task Already_cancelled_metadata_query_does_not_call_database_or_tags(string operation)
    {
        using var fixture = new Fixture("entry");
        fixture.Cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Metadata(operation));
        Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Multi.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    [TestMethod]
    [DataRow("item-detail", "source")]
    [DataRow("item-detail", "source-missing")]
    [DataRow("versions", "source")]
    [DataRow("versions", "source-missing")]
    [DataRow("task-detail", "source")]
    [DataRow("task-detail", "source-missing")]
    public async Task Metadata_query_prioritizes_cancellation_before_missing_result_or_followup_reads(string operation, string stage)
    {
        using var fixture = new Fixture(stage);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Metadata(operation));
        Assert.AreEqual(1, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Multi.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    [TestMethod]
    [DataRow("item-list", "page")]
    [DataRow("task-list", "page")]
    [DataRow("item-list", "tags")]
    [DataRow("item-detail", "tags")]
    [DataRow("versions", "versions-list")]
    public async Task Ignored_page_tag_or_version_list_cancellation_cannot_produce_metadata_response(string operation, string stage)
    {
        using var fixture = new Fixture(stage);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Metadata(operation));
        Assert.AreEqual(operation.EndsWith("list", StringComparison.Ordinal) ? 1 : 0, fixture.Multi.ReceivedCalls().Count());
        Assert.AreEqual(stage == "page" ? 0 : operation == "item-list" ? 1 : 2, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    private sealed class Fixture : IDisposable
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public IMultiResultQueryExecutor Multi { get; } = Substitute.For<IMultiResultQueryExecutor>();
        public ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        public ICommandTransaction Transaction { get; } = Substitute.For<ICommandTransaction>();
        public CancellationTokenSource Cancellation { get; } = new();
        public CountingStream Stream { get; } = new();
        public int FileCalls { get; private set; }
        public bool HandedOff { get; private set; }
        private readonly Guid itemId = Guid.CreateVersion7();
        private readonly Guid versionId = Guid.CreateVersion7();
        private readonly Guid taskId = Guid.CreateVersion7();
        private readonly HostDocumentItemQueryService items;
        private readonly HostDocumentPreviewTaskQueryService tasks;
        private readonly HostDocumentShareManagementService shares;

        public Fixture(string stage)
        {
            var fileId = Guid.CreateVersion7();
            var shareId = Guid.CreateVersion7();
            var now = DateTimeOffset.Parse("2026-10-15T00:00:00Z");
            var files = Substitute.For<IHostFileContentReader>();
            files.OpenReadyContentAsync(fileId, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                FileCalls++;
                if (stage is "file" or "file-failed" or "mime") Cancellation.Cancel();
                if (stage == "file-failed") return Result<HostFileContent>.Failure(new("fixture.file_failure", "fixture", ErrorType.Validation));
                HandedOff = true;
                return Result<HostFileContent>.Success(new(Stream, stage is "mime" or "unsupported" ? "application/octet-stream" : "application/pdf", "result.pdf"));
            });
            Query.QuerySingleOrDefaultAsync<DocumentItemDetailRecord>(DocumentItemSql.FindActiveById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "source" or "source-missing" || stage == "final-document" && HandedOff) Cancellation.Cancel();
                if (stage == "source-missing" || stage == "final-document" && HandedOff) return null;
                return new DocumentItemDetailRecord { Id = itemId, FileId = fileId };
            });
            Query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "source" or "source-missing") Cancellation.Cancel();
                return stage == "source-missing" ? null : new DocumentPreviewTaskRecord
                { Id = taskId, DocumentItemId = itemId, OutputFileId = fileId, StatusKey = HostDocumentPreviewTaskStatusKeys.Succeeded };
            });
            Query.QuerySingleOrDefaultAsync<DocumentVersionRecord>(DocumentItemSql.FindVersionById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "version" or "version-missing") Cancellation.Cancel();
                return stage == "version-missing" ? null : new DocumentVersionRecord { Id = versionId, DocumentItemId = itemId, FileId = fileId };
            });
            Query.QuerySingleOrDefaultAsync<DocumentShareRecord>(DocumentShareSql.FindByCode, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "share-initial" or "share-initial-missing") Cancellation.Cancel();
                if (stage == "share-initial-missing") return null;
                if (stage == "final-share" && HandedOff) { Cancellation.Cancel(); return null; }
                return new DocumentShareRecord { Id = shareId, DocumentId = itemId, IsEnabled = true, ExpireTime = now.AddHours(1), AccessCount = 1,
                    PasswordHash = stage is "password" or "password-denied" ? "fixture-hash" : null };
            });
            var hasher = Substitute.For<IDocumentSharePasswordHasher>();
            hasher.Verify(shareId, "fixture-hash", "fixture-password").Returns(_ => { Cancellation.Cancel(); return stage != "password-denied"; });
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            var database = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            var recorder = new DocumentAccessLogRecorder(Command, Transaction, clock, Substitute.For<IIdGenerator>());
            Query.QueryAsync<DocumentTagAssignmentRow>(DocumentTagAssignmentSql.ListByDocumentItemIds, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage == "tags") Cancellation.Cancel();
                return Array.Empty<DocumentTagAssignmentRow>();
            });
            Query.QueryAsync<DocumentVersionRecord>(DocumentItemSql.ListVersionsByItemId, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage == "versions-list") Cancellation.Cancel();
                return Array.Empty<DocumentVersionRecord>();
            });
            Multi.QueryMultipleAsync(DocumentItemSql.PageSqlServer, Arg.Any<object>(),
                Arg.Any<Func<IMultiResultReader, CancellationToken, Task<(long Total, IReadOnlyList<DocumentItemDetailRecord> Rows)>>>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage == "page") Cancellation.Cancel();
                return (Total: 1L, Rows: (IReadOnlyList<DocumentItemDetailRecord>)new[] { new DocumentItemDetailRecord { Id = itemId, FileId = fileId } });
            });
            Multi.QueryMultipleAsync(DocumentPreviewTaskSql.PageSqlServer, Arg.Any<object>(),
                Arg.Any<Func<IMultiResultReader, CancellationToken, Task<(long Total, IReadOnlyList<DocumentPreviewTaskRecord> Rows)>>>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage == "page") Cancellation.Cancel();
                return (Total: 1L, Rows: (IReadOnlyList<DocumentPreviewTaskRecord>)new[] { new DocumentPreviewTaskRecord { Id = taskId, DocumentItemId = itemId } });
            });
            items = new(Multi, Query, files, recorder, new(Query, Command, null!), database);
            tasks = new(Query, Multi, files, database);
            shares = new(Query, Command, Transaction, null!, items, null!, tasks, clock, Substitute.For<IIdGenerator>(), hasher, recorder);
        }

        public Task<Result<HostFileContent>> Open(string entry) => entry switch
        {
            "current" => items.OpenCurrentVersionContentAsync(itemId, cancellationToken: Cancellation.Token),
            "preview-current" => items.OpenVersionPreviewAsync(itemId, null, cancellationToken: Cancellation.Token),
            "preview-version" => items.OpenVersionPreviewAsync(itemId, versionId, cancellationToken: Cancellation.Token),
            "task" => tasks.OpenOutputContentAsync(taskId, Cancellation.Token),
            "share-current" => shares.OpenAnonymousContentAsync("share", new("fixture-password"), null, Cancellation.Token),
            _ => shares.OpenAnonymousPreviewTaskContentAsync("share", taskId, new("fixture-password"), Cancellation.Token),
        };
        public Task Metadata(string operation) => operation switch
        {
            "item-list" => items.ListAsync(1, 20, cancellationToken: Cancellation.Token),
            "item-detail" => items.GetByIdAsync(itemId, Cancellation.Token),
            "versions" => items.ListVersionsAsync(itemId, Cancellation.Token),
            "task-list" => tasks.ListAsync(1, 20, null, Cancellation.Token),
            _ => tasks.GetAsync(taskId, Cancellation.Token),
        };
        public void Dispose() { Stream.Dispose(); Cancellation.Dispose(); }
    }

    private sealed class CountingStream : MemoryStream
    {
        public int DisposeCalls { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) DisposeCalls++; base.Dispose(disposing); }
    }
}
