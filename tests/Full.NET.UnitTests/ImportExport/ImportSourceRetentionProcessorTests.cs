using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>释放意图可跨失败接续，游标公平推进且租户上下文始终收回。</summary>
[TestClass]
public sealed class ImportSourceRetentionProcessorTests
{
    [TestMethod]
    public async Task Failed_file_release_keeps_intent_and_cannot_starve_a_later_source()
    {
        var fixture = new Fixture(); var first = fixture.Add(); var second = fixture.Add(); var failures = 1;
        fixture.BeforeRelease = id => { if (id == first && failures-- > 0) throw new IOException("storage unavailable"); };
        Assert.AreEqual(0, await fixture.Run());
        Assert.IsNotNull(fixture.Tasks[first].SourceReleaseStartedAtUtc); Assert.IsNull(fixture.Tasks[first].SourceReleasedAtUtc);
        Assert.AreEqual(1, await fixture.Run()); Assert.IsNotNull(fixture.Tasks[second].SourceReleasedAtUtc);
        var reads = fixture.DirectoryReads;
        Assert.AreEqual(1, await fixture.Run()); Assert.AreEqual(2, fixture.DirectoryReads - reads);
        CollectionAssert.AreEqual(new[] { first, second, first }, fixture.Releases);
        Assert.AreEqual(2, fixture.Begins); Assert.IsFalse(fixture.Current.IsAvailable);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionPartial, fixture.Tasks[first].StatusKey);
        Assert.AreEqual(2, fixture.Tasks[first].SucceededRowCount); Assert.AreEqual(4, fixture.Tasks[first].NextLineNumber);
        Assert.IsNotNull(fixture.Tasks[first].ErrorReceiptFileId);
    }

    [TestMethod]
    public async Task Inactive_tenant_is_skipped_without_touching_files_or_hiding_later_tenants()
    {
        var fixture = new Fixture(); var first = fixture.Add(); var second = fixture.Add();
        fixture.Inactive.Add(fixture.Tasks[first].TenantId);
        Assert.AreEqual(0, await fixture.Run()); Assert.IsNull(fixture.Tasks[first].SourceReleaseStartedAtUtc);
        Assert.AreEqual(1, await fixture.Run()); CollectionAssert.AreEqual(new[] { second }, fixture.Releases);
        Assert.IsFalse(fixture.Current.IsAvailable);
    }

    [TestMethod]
    [DataRow("queued")][DataRow("executing")][DataRow("future")][DataRow("released")]
    [DataRow("lease")][DataRow("wrong_tenant")][DataRow("cas_loss")]
    public async Task Changed_task_cannot_be_released_using_stale_directory_metadata(string change)
    {
        var fixture = new Fixture(); var id = fixture.Add();
        fixture.BeforeRead = taskId =>
        {
            var task = fixture.Tasks[taskId];
            fixture.Tasks[taskId] = Copy(task, status: change is "queued" or "executing" ? change : task.StatusKey,
                expires: change == "future" ? fixture.Now.AddDays(1) : task.SourceExpiresAtUtc,
                released: change == "released" ? fixture.Now : null,
                lease: change == "lease" ? fixture.Now.AddMinutes(5) : null,
                tenant: change == "wrong_tenant" ? Guid.CreateVersion7() : task.TenantId);
        };
        if (change == "cas_loss") fixture.BeginAffected = 0;
        Assert.AreEqual(0, await fixture.Run()); Assert.HasCount(0, fixture.Releases);
        Assert.IsFalse(fixture.Current.IsAvailable); Assert.IsNull(fixture.Tasks[id].SourceReleaseStartedAtUtc);
    }

    [TestMethod]
    public async Task Completion_write_failure_reuses_the_same_idempotent_file_release()
    {
        var fixture = new Fixture(); var id = fixture.Add(); fixture.CompletionFailures = 1;
        Assert.AreEqual(0, await fixture.Run()); Assert.IsNotNull(fixture.Tasks[id].SourceReleaseStartedAtUtc);
        Assert.IsNull(fixture.Tasks[id].SourceReleasedAtUtc);
        Assert.AreEqual(1, await fixture.Run()); CollectionAssert.AreEqual(new[] { id, id }, fixture.Releases);
        Assert.AreEqual(1, fixture.Begins); Assert.IsFalse(fixture.Current.IsAvailable);
    }

    [TestMethod]
    public async Task Cancellation_after_intent_propagates_and_leaves_a_recoverable_source()
    {
        var fixture = new Fixture(); var id = fixture.Add(); using var cancellation = new CancellationTokenSource();
        fixture.BeforeRelease = _ => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Run(cancellation.Token));
        Assert.IsNotNull(fixture.Tasks[id].SourceReleaseStartedAtUtc); Assert.IsNull(fixture.Tasks[id].SourceReleasedAtUtc);
        Assert.IsFalse(fixture.Current.IsAvailable); fixture.BeforeRelease = null;
        Assert.AreEqual(1, await fixture.Run());
    }

    private static ImportExportTaskRecord Copy(ImportExportTaskRecord task, string? status = null,
        DateTimeOffset? expires = null, DateTimeOffset? started = null, DateTimeOffset? released = null,
        DateTimeOffset? lease = null, Guid? tenant = null) => new()
    {
        Id = task.Id, TenantId = tenant ?? task.TenantId, SourceFileId = task.SourceFileId,
        SourceExpiresAtUtc = expires ?? task.SourceExpiresAtUtc, SourceReleaseStartedAtUtc = started ?? task.SourceReleaseStartedAtUtc,
        SourceReleasedAtUtc = released ?? task.SourceReleasedAtUtc, StatusKey = status ?? task.StatusKey, LeaseExpiresAtUtc = lease,
        Version = task.Version + 1, SucceededRowCount = task.SucceededRowCount, NextLineNumber = task.NextLineNumber,
        ErrorReceiptFileId = task.ErrorReceiptFileId,
    };

    private sealed class Fixture
    {
        internal DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        internal CurrentTenantAccessor Current { get; } = new();
        internal Dictionary<Guid, ImportExportTaskRecord> Tasks { get; } = [];
        internal HashSet<Guid> Inactive { get; } = [];
        internal List<Guid> Releases { get; } = [];
        internal Action<Guid>? BeforeRead; internal Action<Guid>? BeforeRelease;
        internal int BeginAffected = 1; internal int Begins; internal int CompletionFailures; internal int DirectoryReads;
        private readonly ImportSourceRetentionProcessor processor;

        internal Fixture()
        {
            var queries = new Query(this); var commands = Substitute.For<ICommandExecutor>();
            var files = Substitute.For<ITenantResourceFileStore>(); var tenants = Substitute.For<IActiveTenantContextResolver>();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
            var options = Substitute.For<IOptionsMonitor<ImportExportOptions>>(); options.CurrentValue.Returns(new ImportExportOptions { SourceCleanupBatchSize = 1 });
            tenants.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                Inactive.Contains(call.ArgAt<Guid>(0)) ? null : new TenantContext(call.ArgAt<Guid>(0), "tenant", "Tenant"));
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var parameters = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1); var id = (Guid)parameters["Id"]!;
                Assert.AreEqual(Tasks[id].TenantId, Current.Id); Assert.AreEqual(Tasks[id].SourceFileId, parameters["FileId"]);
                if (ReferenceEquals(call.ArgAt<SqlStatement>(0), ImportSourceRetentionSql.BeginRelease))
                { Begins++; if (BeginAffected == 1) Tasks[id] = Copy(Tasks[id], started: Now); return BeginAffected; }
                if (CompletionFailures-- > 0) throw new IOException("completion unavailable");
                Tasks[id] = Copy(Tasks[id], released: Now); return 1;
            });
            files.ReleaseAsync("import_export", Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var id = call.ArgAt<Guid>(1); Assert.AreEqual(Tasks[id].SourceFileId, call.ArgAt<Guid>(2));
                Assert.IsNotNull(Tasks[id].SourceReleaseStartedAtUtc); Assert.AreEqual(Tasks[id].TenantId, Current.Id);
                Releases.Add(id); BeforeRelease?.Invoke(id); return Task.CompletedTask;
            });
            processor = new(queries, commands, files, tenants, Current, clock, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
                options, new ImportSourceRetentionScanState(), NullLogger<ImportSourceRetentionProcessor>.Instance);
        }

        internal Guid Add()
        {
            var id = Guid.CreateVersion7(); Tasks[id] = new() { Id = id, TenantId = Guid.CreateVersion7(), SourceFileId = Guid.CreateVersion7(),
                SourceExpiresAtUtc = Now.AddHours(-2).AddMinutes(Tasks.Count), StatusKey = ImportExportTaskStatusKeys.ExecutionPartial,
                Version = 1, SucceededRowCount = 2, NextLineNumber = 4, ErrorReceiptFileId = Guid.CreateVersion7() };
            return id;
        }
        internal Task<int> Run(CancellationToken token = default) => processor.ProcessDueAsync(token);

        /// <summary>内部目录记录使用显式替身，避免模拟框架为集合自动生成跨程序集代理。</summary>
        private sealed class Query(Fixture owner) : IQueryExecutor
        {
            public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            {
                Assert.AreEqual(typeof(ImportExportTaskRecord), typeof(T)); Assert.AreSame(ImportExportTaskSql.FindById, statement);
                var id = (Guid)((IReadOnlyDictionary<string, object?>)parameters!)["Id"]!;
                Assert.IsTrue(owner.Current.IsAvailable && !owner.Current.IsHost); owner.BeforeRead?.Invoke(id);
                return Task.FromResult((T?)(object?)owner.Tasks.GetValueOrDefault(id));
            }

            public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            {
                Assert.AreEqual(typeof(ImportSourceRetentionCandidate), typeof(T)); owner.DirectoryReads++;
                var values = (IReadOnlyDictionary<string, object?>)parameters!;
                var after = values["AfterExpiresAtUtc"] as DateTimeOffset?; var afterId = values["AfterId"] as Guid?;
                var rows = owner.Tasks.Values.Where(task => task.SourceReleasedAtUtc is null && task.SourceExpiresAtUtc <= owner.Now
                        && (!after.HasValue || task.SourceExpiresAtUtc > after || task.SourceExpiresAtUtc == after && task.Id.CompareTo(afterId) > 0))
                    .OrderBy(task => task.SourceExpiresAtUtc).ThenBy(task => task.Id).Take(1)
                    .Select(task => new ImportSourceRetentionCandidate { Id = task.Id, TenantId = task.TenantId, SourceExpiresAtUtc = task.SourceExpiresAtUtc }).ToArray();
                return Task.FromResult((IReadOnlyList<T>)(object)rows);
            }
        }
    }
}
