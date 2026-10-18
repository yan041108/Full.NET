using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>自定义 Provider 仍经过统一实际字节预算，Worker 自身拥有源流和成功输出。</summary>
[TestClass]
public sealed class DocumentPreviewWorkerBudgetTests
{
    [TestMethod]
    [DataRow("input", "document.office_preview.input_too_large")]
    [DataRow("output", "document.office_preview.output_too_large")]
    [DataRow("declared-output", "document.office_preview.output_too_large")]
    public async Task Custom_provider_cannot_bypass_source_or_pdf_budget(string mode, string error)
    {
        using var fixture = new Fixture(mode);
        Assert.AreEqual(1, await fixture.Runner.ProcessPendingAsync(default));
        CollectionAssert.AreEqual(new[] { error }, fixture.Errors);
        Assert.IsTrue(fixture.Source.Disposed);
        Assert.IsTrue(fixture.Output.Disposed || mode == "input");
        Assert.AreEqual(0, fixture.Successes);
        if (mode == "declared-output") Assert.AreEqual(0, fixture.Uploads);
        Assert.IsFalse(System.IO.Directory.EnumerateFileSystemEntries(fixture.Root).Any());
    }

    [TestMethod]
    public async Task Boundary_sized_output_is_uploaded_then_marked_successful_and_streams_are_released()
    {
        using var fixture = new Fixture("success");
        Assert.AreEqual(1, await fixture.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(1, fixture.Uploads); Assert.AreEqual(1, fixture.Successes);
        Assert.AreEqual(0, fixture.Errors.Count);
        Assert.IsTrue(fixture.Source.Disposed); Assert.IsTrue(fixture.Output.Disposed);
        Assert.IsFalse(System.IO.Directory.EnumerateFileSystemEntries(fixture.Root).Any());
    }

    [TestMethod]
    public async Task Worker_stop_propagates_cancellation_without_failed_status_write()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture("cancel", cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Runner.ProcessPendingAsync(cancellation.Token));
        Assert.AreEqual(0, fixture.Errors.Count); Assert.AreEqual(0, fixture.Successes); Assert.AreEqual(0, fixture.Uploads);
        Assert.IsTrue(fixture.Source.Disposed);
        Assert.IsFalse(System.IO.Directory.EnumerateFileSystemEntries(fixture.Root).Any());
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "fullnet-preview-worker-probe");
        public string Root { get; } = Path.Combine(Workspace, Guid.NewGuid().ToString("N"));
        public TrackedStream Source { get; }
        public TrackedStream Output { get; }
        public List<string> Errors { get; } = [];
        public int Successes { get; private set; }
        public int Uploads { get; private set; }
        public DocumentPreviewTaskRunner Runner { get; }

        public Fixture(string mode, CancellationTokenSource? cancellation = null)
        {
            System.IO.Directory.CreateDirectory(Root);
            Source = new(mode == "input" ? [1, 2, 3, 4] : [1]);
            Output = new(mode is "output" or "declared-output" ? [1, 2, 3, 4] : [1, 2, 3]);
            var queries = Substitute.For<IQueryExecutor>();
            var commands = Substitute.For<ICommandExecutor>();
            var reader = Substitute.For<IHostFileContentReader>();
            var writer = Substitute.For<IHostFileUploadWriter>();
            var options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions
            {
                Enabled = true, ProviderKey = "external_http", MaxInputBytes = 3, MaxOutputBytes = 3, TempRootPath = Root,
            });
            var claimed = false;
            DocumentPreviewTaskRecord? ownedTask = null;
            queries.QueryAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.ClaimPendingSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    if (claimed) return Array.Empty<DocumentPreviewTaskRecord>();
                    claimed = true;
                    var parameters = (Dictionary<string, object?>)info.ArgAt<object>(1);
                    ownedTask = new DocumentPreviewTaskRecord { Id = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), SourceFileId = Guid.NewGuid(), StatusKey = "processing",
                        LeaseId = (Guid)parameters["LeaseId"]!, LeaseExpiresAtUtc = (DateTimeOffset)parameters["LeaseExpiresAtUtc"]! };
                    return new[] { ownedTask };
                });
            queries.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.LockCompletionSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ => ownedTask);
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    var action = info.Arg<Func<CancellationToken, Task<bool>>>();
                    Assert.IsNotNull(action);
                    return action(info.Arg<CancellationToken>());
                });
            reader.OpenReadyContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Result<HostFileContent>.Success(new(Source, "application/msword", "source.doc")));
            var provider = new Converter(async (input, token) =>
            {
                if (mode == "cancel") { cancellation!.Cancel(); token.ThrowIfCancellationRequested(); }
                await input.SourceContent.CopyToAsync(Stream.Null, token);
                return Result<DocumentOfficePreviewConversionOutput>.Success(new(Output, mode == "declared-output" ? 4 : 3));
            });
            writer.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(async info =>
                {
                    Uploads++;
                    var content = info.Arg<Stream>();
                    Assert.IsNotNull(content);
                    await content.CopyToAsync(Stream.Null, info.Arg<CancellationToken>());
                    return Result<HostFileUploadResult>.Success(new(Guid.NewGuid(), 3, null));
                });
            commands.ExecuteAsync(DocumentPreviewTaskSql.MarkFailed, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                Errors.Add((string)((Dictionary<string, object?>)info.ArgAt<object>(1))["ErrorCode"]!); return 1;
            });
            commands.ExecuteAsync(DocumentPreviewTaskSql.MarkSucceeded, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ => { Successes++; return 1; });
            Runner = new(queries, commands, transaction, reader, writer,
                new DocumentOfficePreviewConversionProviderResolver([provider], options), clock,
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), options);
        }

        public void Dispose()
        {
            Source.Dispose(); Output.Dispose();
            var fullPath = Path.GetFullPath(Root);
            if (!fullPath.StartsWith(Path.GetFullPath(Workspace) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Worker 夹具目录越界。");
            System.IO.Directory.Delete(fullPath, true);
        }
    }

    private sealed class Converter(Func<DocumentOfficePreviewConversionInput, CancellationToken, Task<Result<DocumentOfficePreviewConversionOutput>>> convert)
        : IDocumentOfficePreviewConversionProvider
    {
        public string ProviderKey => "external_http";
        public Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(DocumentOfficePreviewConversionInput request, string workingDirectory, CancellationToken cancellationToken = default) => convert(request, cancellationToken);
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}
