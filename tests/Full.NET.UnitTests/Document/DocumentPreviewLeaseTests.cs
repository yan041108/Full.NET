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

/// <summary>用权威时钟变化模拟过期，不用等待真实租约时间；领取、上传和完成各边界均须收口。</summary>
[TestClass]
public sealed class DocumentPreviewLeaseTests
{
    [TestMethod]
    [DataRow("open")]
    [DataRow("convert")]
    [DataRow("upload")]
    [DataRow("conversion-failure")]
    [DataRow("fault")]
    public async Task Expired_work_cannot_publish_success_or_generic_failure(string stage)
    {
        using var f = new Fixture(stage);
        Assert.AreEqual(1, await f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Successes); Assert.AreEqual(0, f.Failures);
        Assert.AreEqual(stage == "upload" ? 1 : 0, f.Uploads);
        Assert.IsTrue(f.Source.Disposed);
        if (stage is "convert" or "upload") Assert.IsTrue(f.Output.Disposed);
        Assert.AreEqual(2, f.RecoveryCalls);
    }

    [TestMethod]
    public async Task Sequential_batch_claims_distinct_leases_only_when_next_task_can_start()
    {
        using var f = new Fixture("success", 2);
        Assert.AreEqual(2, await f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(2, f.Successes);
        Assert.AreEqual(2, f.Leases.Count);
        Assert.AreNotEqual(f.Leases[0].Id, f.Leases[1].Id);
        Assert.IsTrue(f.Leases[1].Expires > f.Leases[0].Expires);
        Assert.IsTrue(f.ClaimSizes.All(size => size == 1));
    }

    [TestMethod]
    public async Task Live_failure_write_carries_own_lease_and_current_completion_time()
    {
        using var f = new Fixture("live-failure");
        Assert.AreEqual(1, await f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(1, f.Failures); Assert.AreEqual(0, f.Successes);
        Assert.AreEqual(f.Leases.Single().Id, (Guid)f.LastFailure!["LeaseId"]!);
        Assert.AreEqual(f.Now, f.LastFailure["Now"]);
        Assert.AreEqual(f.Now, f.LastFailure["CompletedAtUtc"]);
    }

    [TestMethod]
    [DataRow("lock-success")]
    [DataRow("lock-failure")]
    public async Task Completion_rechecks_time_after_waiting_for_task_lock(string stage)
    {
        using var f = new Fixture(stage);
        Assert.AreEqual(1, await f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Successes); Assert.AreEqual(0, f.Failures);
        Assert.IsTrue(f.Source.Disposed);
    }

    [TestMethod]
    [DataRow("missing-identity")]
    [DataRow("wrong-identity")]
    [DataRow("missing-expiry")]
    public async Task Malformed_claim_is_rejected_before_opening_source(string stage)
    {
        using var f = new Fixture(stage);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(0, f.Opened); Assert.AreEqual(0, f.Uploads); Assert.AreEqual(0, f.Successes);
    }

    [TestMethod]
    public async Task Empty_queue_still_runs_bounded_expiry_for_abandoned_processing_rows()
    {
        using var f = new Fixture("success", 0);
        Assert.AreEqual(0, await f.Runner.ProcessPendingAsync(default));
        Assert.AreEqual(2, f.RecoveryCalls); Assert.AreEqual(0, f.Opened);
        Assert.AreEqual(2, f.LastRecovery!["BatchSize"]);
        Assert.AreEqual(f.Now.AddSeconds(-240), f.LastRecovery["LegacyCutoff"]);
        Assert.AreEqual("document.office_preview.lease_expired", f.LastRecovery["ErrorCode"]);
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(149, false)]
    [DataRow(150, true)]
    [DataRow(7200, true)]
    [DataRow(7201, false)]
    [DataRow(-1, false)]
    public void Lease_configuration_preserves_automatic_default_and_limits_explicit_budgets(int seconds, bool valid)
    {
        var value = new DocumentOfficePreviewConversionOptions { LeaseDurationSeconds = seconds };
        Assert.AreEqual(valid, new DocumentOfficePreviewConversionOptionsValidator().Validate(null, value).Succeeded);
    }

    [TestMethod]
    public void Existing_long_conversion_timeout_gets_compatible_automatic_lease()
    {
        var value = new DocumentOfficePreviewConversionOptions { TimeoutSeconds = 3600 };
        Assert.AreEqual(3720, value.EffectiveLeaseSeconds);
        Assert.IsTrue(new DocumentOfficePreviewConversionOptionsValidator().Validate(null, value).Succeeded);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "fullnet-preview-lease-probe");
        private readonly string root = Path.Combine(Workspace, Guid.NewGuid().ToString("N"));
        public DateTimeOffset Now { get; private set; } = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        public TrackedStream Source { get; private set; } = new([1]);
        public TrackedStream Output { get; private set; } = new([1]);
        public List<(Guid Id, DateTimeOffset Expires)> Leases { get; } = [];
        public List<int> ClaimSizes { get; } = [];
        public int Successes { get; private set; }
        public int Failures { get; private set; }
        public int Uploads { get; private set; }
        public int Opened { get; private set; }
        public int RecoveryCalls { get; private set; }
        public Dictionary<string, object?>? LastRecovery { get; private set; }
        public Dictionary<string, object?>? LastFailure { get; private set; }
        public DocumentPreviewTaskRunner Runner { get; }

        public Fixture(string stage, int tasks = 1)
        {
            System.IO.Directory.CreateDirectory(root);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(_ => Now);
            var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
            var reader = Substitute.For<IHostFileContentReader>(); var writer = Substitute.For<IHostFileUploadWriter>();
            var monitor = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
            monitor.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions { Enabled = true, ProviderKey = "external_http", BatchSize = 2, TempRootPath = root });
            DocumentPreviewTaskRecord? ownedTask = null;
            queries.QueryAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.ClaimPendingSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                if (tasks-- <= 0) return Array.Empty<DocumentPreviewTaskRecord>();
                var p = (Dictionary<string, object?>)info.ArgAt<object>(1);
                var lease = (Guid)p["LeaseId"]!; var expiry = (DateTimeOffset)p["LeaseExpiresAtUtc"]!;
                Leases.Add((lease, expiry)); ClaimSizes.Add((int)p["BatchSize"]!);
                ownedTask = new DocumentPreviewTaskRecord { Id = Guid.CreateVersion7(), SourceFileId = Guid.CreateVersion7(), RequestedByUserId = Guid.CreateVersion7(),
                    StatusKey = "processing", LeaseId = stage == "missing-identity" ? null : stage == "wrong-identity" ? Guid.CreateVersion7() : lease,
                    LeaseExpiresAtUtc = stage == "missing-expiry" ? null : expiry };
                return new[] { ownedTask };
            });
            queries.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.LockCompletionSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage.StartsWith("lock-", StringComparison.Ordinal)) Now = Now.AddSeconds(241);
                return ownedTask;
            });
            reader.OpenReadyContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Opened++; Source.Dispose(); Source = new([1]);
                if (stage == "open") Now = Now.AddSeconds(241);
                return Result<HostFileContent>.Success(new(Source, "application/msword", "source.doc"));
            });
            var converter = new Converter(async (input, token) =>
            {
                await input.SourceContent.CopyToAsync(Stream.Null, token);
                Now = Now.AddSeconds(stage is "convert" or "conversion-failure" or "fault" ? 241 : 20);
                if (stage == "fault") throw new IOException("fixture conversion fault");
                if (stage is "conversion-failure" or "live-failure" or "lock-failure") return Result<DocumentOfficePreviewConversionOutput>.Failure(
                    new("document.office_preview.conversion_failed", "fixture failure", ErrorType.BusinessRule));
                Output.Dispose(); Output = new([1]);
                return Result<DocumentOfficePreviewConversionOutput>.Success(new(Output, 1));
            });
            writer.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(async info =>
            {
                Uploads++;
                var content = info.Arg<Stream>();
                Assert.IsNotNull(content);
                await content.CopyToAsync(Stream.Null, info.Arg<CancellationToken>());
                if (stage == "upload") Now = Now.AddSeconds(241);
                return Result<HostFileUploadResult>.Success(new(Guid.CreateVersion7(), 1, null));
            });
            commands.ExecuteAsync(DocumentPreviewTaskSql.MarkSucceeded, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ => { Successes++; return 1; });
            commands.ExecuteAsync(DocumentPreviewTaskSql.MarkFailed, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                Failures++; LastFailure = (Dictionary<string, object?>)info.ArgAt<object>(1); return 1;
            });
            commands.ExecuteAsync(DocumentPreviewTaskSql.ExpireProcessingSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                RecoveryCalls++; LastRecovery = (Dictionary<string, object?>)info.ArgAt<object>(1); return 0;
            });
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(info =>
                {
                    var action = info.Arg<Func<CancellationToken, Task<bool>>>();
                    Assert.IsNotNull(action);
                    return action(info.Arg<CancellationToken>());
                });
            Runner = new(queries, commands, transaction, reader, writer,
                new DocumentOfficePreviewConversionProviderResolver([converter], monitor), clock,
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), monitor);
        }

        public void Dispose()
        {
            Source.Dispose(); Output.Dispose();
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(Path.GetFullPath(Workspace) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("租约夹具目录越界。");
            System.IO.Directory.Delete(full, true);
        }
    }

    public sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Converter(Func<DocumentOfficePreviewConversionInput, CancellationToken, Task<Result<DocumentOfficePreviewConversionOutput>>> convert) : IDocumentOfficePreviewConversionProvider
    {
        public string ProviderKey => "external_http";
        public Task<Result<DocumentOfficePreviewConversionOutput>> ConvertAsync(DocumentOfficePreviewConversionInput request, string workingDirectory, CancellationToken cancellationToken = default) => convert(request, cancellationToken);
    }
}
