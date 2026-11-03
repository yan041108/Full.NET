using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.DocumentAccessLogs;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Full.NET.Modules.Files.Contracts;
using ErrorCodes = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>通过存储停点验证输出移交的并发边界及异常时的流所有权。</summary>
[TestClass]
public sealed class DocumentPreviewOutputBoundaryTests
{
    [TestMethod]
    [DataRow("revoked")]
    [DataRow("document_deleted")]
    [DataRow("task_removed")]
    [DataRow("output_changed")]
    [DataRow("status_changed")]
    [DataRow("cancelled")]
    public async Task Change_while_storage_is_opening_refuses_transfer_and_disposes_stream(string change)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        if (change == "cancelled")
        {
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.ReadAfterAsync(cancellation.Cancel, cancellation.Token));
            Assert.AreEqual(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var result = await fixture.ReadAfterAsync(() => fixture.State.Change = change);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(change == "revoked" ? ErrorCodes.AccessDenied : ErrorCodes.PreviewTaskNotFound, result.Error!.Code);
        }
        Assert.AreEqual(1, fixture.Stream.AsyncDisposals, "拿不到流的调用方不能承担释放责任。");
        Assert.IsFalse(fixture.Stream.CanRead);
        Assert.HasCount(0, fixture.Commands.Statements, "拒绝读取不能写入成功访问事实。");
        Assert.AreEqual(0, fixture.Transaction.Commits);
    }

    [TestMethod]
    [DataRow("policy")]
    [DataRow("item_query")]
    [DataRow("task_query")]
    [DataRow("log_insert")]
    [DataRow("log_commit")]
    public async Task Exception_after_storage_open_preserves_error_and_releases_untransferred_stream(string phase)
    {
        using var fixture = new Fixture();
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ReadAfterAsync(() => fixture.State.Fault = phase));
        Assert.AreSame(fixture.State.Failure, error);
        Assert.AreEqual(1, fixture.Stream.AsyncDisposals);
        Assert.IsFalse(fixture.Stream.CanRead);
        Assert.AreEqual(0, fixture.Transaction.Commits);
        if (phase is "policy" or "item_query" or "task_query") Assert.HasCount(0, fixture.Commands.Statements);
    }

    [TestMethod]
    public async Task Successful_read_transfers_exact_snapshot_stream_and_records_access_once()
    {
        using var fixture = new Fixture();
        var result = await fixture.ReadAfterAsync(() => { });
        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(fixture.Stream, result.Value!.Content);
        Assert.AreEqual(fixture.State.OutputId, fixture.Reader.OpenedFileId);
        Assert.AreEqual(0, fixture.Stream.AsyncDisposals, "成功移交后由调用方持有流。");
        Assert.AreEqual(1, fixture.Stream.ReadByte());
        Assert.AreEqual(1, fixture.Commands.Statements.Count(s => ReferenceEquals(s, DocumentAccessLogSql.Insert)));
        Assert.AreEqual(1, fixture.Transaction.Commits);
        await result.Value.Content.DisposeAsync();
        Assert.AreEqual(1, fixture.Stream.AsyncDisposals);
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("exception")]
    public async Task Storage_failure_does_not_revalidate_or_record_success(string mode)
    {
        using var fixture = new Fixture();
        fixture.Reader.FailureMode = mode;
        if (mode == "exception")
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ReadAfterAsync(() => { }));
            Assert.AreSame(fixture.State.Failure, error);
        }
        else
        {
            var result = await fixture.ReadAfterAsync(() => { });
            Assert.IsFalse(result.IsSuccess);
            Assert.AreSame(fixture.Reader.StorageError, result.Error);
        }
        Assert.AreEqual(1, fixture.Policy.Checks);
        Assert.AreEqual(1, fixture.Queries.TaskReads);
        Assert.AreEqual(0, fixture.Stream.AsyncDisposals, "存储未移交的资源仍归存储适配器所有。");
        Assert.HasCount(0, fixture.Commands.Statements);
    }

    private sealed class Fixture : IDisposable
    {
        internal State State { get; } = new();
        internal OwnedStream Stream { get; } = new();
        internal GateReader Reader { get; }
        internal Queries Queries { get; }
        internal Policy Policy { get; }
        internal Commands Commands { get; }
        internal Transaction Transaction { get; }
        private HostDocumentPreviewTaskQueryService Service { get; }

        internal Fixture()
        {
            Reader = new(State, Stream); Queries = new(State); Policy = new(State);
            Commands = new(State); Transaction = new(State);
            var logs = new DocumentAccessLogRecorder(Commands, Transaction, new FixedClock(), new GuidV7IdGenerator());
            Service = new(Queries, null!, Reader, null!, Policy, logs);
        }

        internal async Task<Result<HostFileContent>> ReadAfterAsync(Action change, CancellationToken token = default)
        {
            var read = Service.OpenOutputContentAsync(State.TaskId, token);
            try
            {
                await Reader.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                change();
                Reader.Resume.TrySetResult();
                return await read.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Reader.Resume.TrySetResult();
                // 断言或超时失败也先结束受控读取，再释放夹具，避免后台 continuation 访问已释放资源。
                if (!read.IsCompleted)
                    try { await read.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            }
        }

        public void Dispose() => Stream.Dispose();
    }

    private sealed class State
    {
        internal Guid TaskId { get; } = Guid.CreateVersion7();
        internal Guid DocumentId { get; } = Guid.CreateVersion7();
        internal Guid OutputId { get; } = Guid.CreateVersion7();
        internal string? Change { get; set; }
        internal string? Fault { get; set; }
        internal InvalidOperationException Failure { get; } = new("Controlled boundary failure.");
    }

    private sealed class GateReader(State state, OwnedStream stream) : IHostFileContentReader
    {
        internal TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? FailureMode { get; set; }
        internal Guid? OpenedFileId { get; private set; }
        internal Error StorageError { get; } = new("test.storage.unavailable", "Controlled storage failure.", ErrorType.NotFound);
        public async Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            OpenedFileId = fileId;
            Opened.TrySetResult();
            // 模拟已打开流但暂未返回的外部存储，允许取消恰好发生在移交前。
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (FailureMode == "exception") throw state.Failure;
            if (FailureMode == "failure") return Result<HostFileContent>.Failure(StorageError);
            return Result<HostFileContent>.Success(new(stream, "application/pdf", "preview.pdf"));
        }
    }

    private sealed class Queries(State state) : IQueryExecutor
    {
        internal int TaskReads { get; private set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value;
            if (typeof(T) == typeof(DocumentPreviewTaskRecord))
            {
                if (++TaskReads > 1 && state.Fault == "task_query") throw state.Failure;
                value = TaskReads > 1 && state.Change == "task_removed" ? null : new DocumentPreviewTaskRecord {
                    Id = state.TaskId, DocumentItemId = state.DocumentId, DocumentTitle = "Boundary document",
                    StatusKey = TaskReads > 1 && state.Change == "status_changed" ? "failed" : "succeeded",
                    OutputFileId = TaskReads > 1 && state.Change == "output_changed" ? Guid.CreateVersion7() : state.OutputId };
            }
            else if (typeof(T) == typeof(DocumentItemDetailRecord))
            {
                if (state.Fault == "item_query") throw state.Failure;
                value = state.Change == "document_deleted" ? null : new DocumentItemDetailRecord { Id = state.DocumentId };
            }
            else throw new InvalidOperationException("Unexpected query projection.");
            return Task.FromResult<T?>(value is null ? default : (T)value);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unexpected list query.");
    }

    private sealed class Policy(State state) : IDocumentAccessPolicy
    {
        internal int Checks { get; private set; }
        public Guid? ActorUserId { get; } = Guid.CreateVersion7();
        public Task<Error?> CheckAsync(Guid documentId, DocumentOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++Checks > 1 && state.Fault == "policy") throw state.Failure;
            return Task.FromResult<Error?>(Checks > 1 && state.Change == "revoked"
                ? new(ErrorCodes.AccessDenied, "Controlled revocation.", ErrorType.Forbidden) : null);
        }
    }

    private sealed class Commands(State state) : ICommandExecutor
    {
        internal List<SqlStatement> Statements { get; } = [];
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Statements.Add(statement);
            if (state.Fault == "log_insert" && ReferenceEquals(statement, DocumentAccessLogSql.Insert)) throw state.Failure;
            return Task.FromResult(1);
        }
    }

    private sealed class Transaction(State state) : ICommandTransaction
    {
        internal int Commits { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            var result = await action(cancellationToken);
            if (state.Fault == "log_commit") throw state.Failure;
            Commits++;
            return result;
        }
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero); }

    private sealed class OwnedStream() : MemoryStream([1, 2, 3])
    {
        internal int AsyncDisposals { get; private set; }
        public override ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
