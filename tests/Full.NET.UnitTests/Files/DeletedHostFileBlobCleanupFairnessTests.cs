using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Cleanup;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class DeletedHostFileBlobCleanupFairnessTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Failed_first_page_does_not_starve_later_tombstones_and_is_revisited(DatabaseProvider provider)
    {
        var first = new DeletedHostFileBlobRecord(Guid.CreateVersion7(), "local", "failed", DateTimeOffset.UtcNow.AddHours(-2));
        var second = new DeletedHostFileBlobRecord(Guid.CreateVersion7(), "local", "later", first.DeletedAtUtc.AddMinutes(1));
        var rows = new List<DeletedHostFileBlobRecord> { first, second };
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var p = (IReadOnlyDictionary<string, object?>)call[1]!;
                return Task.FromResult<IReadOnlyList<DeletedHostFileBlobRecord>>(rows
                    .Where(r => (int)p["HasCursor"]! == 0 || r.DeletedAtUtc > (DateTimeOffset)p["AfterDeletedAtUtc"]!)
                    .Take((int)p["BatchSize"]!).ToArray());
            });
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = (Guid)((IReadOnlyDictionary<string, object?>)call[1]!)["FileId"]!;
            return Task.FromResult(rows.RemoveAll(r => r.Id == id));
        });
        var blobs = new TestStorage();
        var runner = Create(provider, query, commands, blobs);
        var options = new DeletedHostFileBlobCleanupOptions { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1 };
        Assert.AreEqual(1, (await runner.RunOnceAsync(options, default)).BlobFailures);
        Assert.AreEqual(1, (await runner.RunOnceAsync(options, default)).Purged, "首批不可删除对象不得永久阻塞后续墓碑。");
        Assert.AreEqual(0, (await runner.RunOnceAsync(options, default)).Scanned);
        Assert.AreEqual(1, (await runner.RunOnceAsync(options, default)).BlobFailures, "周期完成后仍应重试保留墓碑。");
        CollectionAssert.AreEqual(new[] { "failed", "later", "failed" }, blobs.Deletes);
    }

    [TestMethod]
    [DataRow(0, 1, 5)]
    [DataRow(1001, 1, 5)]
    [DataRow(1, 0, 5)]
    [DataRow(1, 101, 5)]
    [DataRow(1, 1, 4)]
    [DataRow(1, 1, 86401)]
    public async Task Invalid_runtime_bounds_fail_before_database_or_storage(int batch, int rounds, int poll)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DeletedHostFileBlobRecord>>([]));
        var commands = Substitute.For<ICommandExecutor>(); var blobs = new TestStorage();
        var runner = Create(DatabaseProvider.SqlServer, query, commands, blobs);
        await Assert.ThrowsAsync<OptionsValidationException>(() => runner.RunOnceAsync(new()
            { Enabled = true, BatchSize = batch, MaxBatchesPerRun = rounds, PollSeconds = poll }, default));
        Assert.HasCount(0, query.ReceivedCalls()); Assert.HasCount(0, commands.ReceivedCalls()); Assert.HasCount(0, blobs.Deletes);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Scan_cutoff_is_frozen_until_the_cycle_ends(DatabaseProvider provider)
    {
        var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
        var start = clock.UtcNow; var row = new DeletedHostFileBlobRecord(Guid.CreateVersion7(), "local", "failed", start.AddMinutes(-1));
        var reads = new List<IReadOnlyDictionary<string, object?>>(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var p = (IReadOnlyDictionary<string, object?>)call[1]!; reads.Add(p);
            return Task.FromResult<IReadOnlyList<DeletedHostFileBlobRecord>>((int)p["HasCursor"]! == 0 ? [row] : []);
        });
        var runner = Create(provider, query, Substitute.For<ICommandExecutor>(), new TestStorage(), clock);
        var options = new DeletedHostFileBlobCleanupOptions { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1 };
        await runner.RunOnceAsync(options, default); clock.UtcNow = start.AddHours(1);
        await runner.RunOnceAsync(options, default); await runner.RunOnceAsync(options, default);
        Assert.AreEqual(start, reads[0]["ScanThroughDeletedAtUtc"]); Assert.AreEqual(start, reads[1]["ScanThroughDeletedAtUtc"]);
        Assert.AreEqual(clock.UtcNow, reads[2]["ScanThroughDeletedAtUtc"]); Assert.AreEqual(0, reads[2]["HasCursor"]);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Database_failure_preserves_batch_position_for_idempotent_retry(DatabaseProvider provider)
    {
        var row = new DeletedHostFileBlobRecord(Guid.CreateVersion7(), "local", "later", DateTimeOffset.UtcNow.AddMinutes(-1));
        var reads = new List<IReadOnlyDictionary<string, object?>>(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            reads.Add((IReadOnlyDictionary<string, object?>)call[1]!);
            return Task.FromResult<IReadOnlyList<DeletedHostFileBlobRecord>>([row]);
        });
        var commands = Substitute.For<ICommandExecutor>(); var attempts = 0;
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++attempts == 1 ? Task.FromException<int>(new IOException("数据库写入失败")) : Task.FromResult(1));
        var blobs = new TestStorage(); var runner = Create(provider, query, commands, blobs);
        var options = new DeletedHostFileBlobCleanupOptions { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1 };
        await Assert.ThrowsAsync<IOException>(() => runner.RunOnceAsync(options, default));
        Assert.AreEqual(1, (await runner.RunOnceAsync(options, default)).Purged);
        Assert.IsNull(reads[0]["AfterId"]); Assert.IsNull(reads[1]["AfterId"]);
        Assert.AreEqual(reads[0]["ScanThroughDeletedAtUtc"], reads[1]["ScanThroughDeletedAtUtc"]);
        CollectionAssert.AreEqual(new[] { "later", "later" }, blobs.Deletes);
    }

    [TestMethod]
    public async Task Cancelled_waiter_does_not_enter_or_release_another_scan()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = Substitute.For<IQueryExecutor>(); var calls = 0;
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            calls++; entered.TrySetResult(); await resume.Task; return (IReadOnlyList<DeletedHostFileBlobRecord>)[];
        });
        var runner = Create(DatabaseProvider.SqlServer, query, Substitute.For<ICommandExecutor>(), new TestStorage());
        var options = new DeletedHostFileBlobCleanupOptions { Enabled = true };
        var first = runner.RunOnceAsync(options, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); using var cancellation = new CancellationTokenSource();
            var waiting = runner.RunOnceAsync(options, cancellation.Token); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiting); Assert.AreEqual(1, calls);
        }
        finally { resume.TrySetResult(); await first; }
        await runner.RunOnceAsync(options, default); Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task Disabled_cleanup_never_opens_database_or_storage()
    {
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var blobs = new TestStorage();
        var result = await Create(DatabaseProvider.SqlServer, query, commands, blobs).RunOnceAsync(new() { Enabled = false, BatchSize = 0 }, default);
        Assert.AreEqual(DeletedHostFileBlobCleanupResult.Empty, result);
        Assert.HasCount(0, query.ReceivedCalls()); Assert.HasCount(0, commands.ReceivedCalls()); Assert.HasCount(0, blobs.Deletes);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cancellation_combined_with_blob_failure_preserves_retry_position(DatabaseProvider provider)
    {
        using var cancellation = new CancellationTokenSource();
        var row = new DeletedHostFileBlobRecord(Guid.CreateVersion7(), "local", "failed", DateTimeOffset.UtcNow.AddMinutes(-1));
        var reads = new List<IReadOnlyDictionary<string, object?>>(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<DeletedHostFileBlobRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            reads.Add((IReadOnlyDictionary<string, object?>)call[1]!);
            return Task.FromResult<IReadOnlyList<DeletedHostFileBlobRecord>>([row]);
        });
        var blobs = new TestStorage { OnDelete = cancellation.Cancel };
        var runner = Create(provider, query, Substitute.For<ICommandExecutor>(), blobs);
        var options = new DeletedHostFileBlobCleanupOptions { Enabled = true, BatchSize = 1, MaxBatchesPerRun = 1 };
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunOnceAsync(options, cancellation.Token));
        blobs.OnDelete = null;
        Assert.AreEqual(1, (await runner.RunOnceAsync(options, default)).BlobFailures);
        Assert.IsNull(reads[0]["AfterId"]); Assert.IsNull(reads[1]["AfterId"]);
    }

    private static DeletedHostFileBlobCleanupRunner Create(DatabaseProvider provider, IQueryExecutor query, ICommandExecutor commands, TestStorage blobs, IClock? clock = null) =>
        new(query, commands, new FileStorageProviderRegistry([blobs], Options.Create(new FileStorageOptions())),
            Options.Create(new DatabaseOptions { Provider = provider }), clock);

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }

    private sealed class TestStorage : IFileStorageProvider
    {
        public string ProviderKey => "local";
        public List<string> Deletes { get; } = [];
        internal Action? OnDelete { get; set; }
        public Task SaveAsync(string key, Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deletes.Add(key);
            OnDelete?.Invoke();
            return key == "failed" ? Task.FromException(new IOException("不可删除的首批对象")) : Task.CompletedTask;
        }
    }
}
