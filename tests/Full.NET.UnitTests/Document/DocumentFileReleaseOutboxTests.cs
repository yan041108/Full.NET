using System.Diagnostics.CodeAnalysis;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Features.ManageHostDocumentItems;
using Full.NET.Modules.Document.Features.QueryHostRecycleBin;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Features.HostFileReferences;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>验证删除事实与本地状态原子提交，Files 故障不能消除可靠释放任务。</summary>
[TestClass]
public sealed class DocumentFileReleaseOutboxTests
{
    [TestMethod]
    [DataRow("manual")]
    [DataRow("retention")]
    public async Task Deletion_records_release_fact_without_synchronous_files_call(string mode)
    {
        using var f = new Fixture();
        Assert.IsTrue(await f.DeleteAsync(mode));
        Assert.AreEqual(1, f.Events.Count);
        Assert.AreEqual(new DocumentVersionDeletedIntegrationEvent(f.Item, f.Target, f.File), f.Events[0].Payload);
        Assert.IsTrue(f.Events.All(e => e.InTransaction));
        Assert.AreEqual(0, f.ReleaseCalls);
        Assert.IsFalse(f.Versions.Any(v => v.Id == f.Target));
    }

    [TestMethod]
    [DataRow("manual")]
    [DataRow("retention")]
    public async Task Outbox_failure_rolls_back_deleted_version_and_audit(string mode)
    {
        using var f = new Fixture { FailEventAt = 1 };
        await Assert.ThrowsExactlyAsync<IOException>(() => f.DeleteAsync(mode));
        Assert.AreEqual(3, f.Versions.Count);
        Assert.AreEqual(0, f.Audits);
        Assert.AreEqual(0, f.Events.Count);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Purge_records_all_version_facts_before_removing_dependents(DatabaseProvider provider)
    {
        using var f = new Fixture(provider, 135);
        var ids = f.Versions.Select(v => v.Id).ToArray();
        Assert.IsTrue(await f.DeleteAsync("purge"));
        Assert.AreEqual(135, f.Events.Count);
        Assert.IsTrue(f.Events.All(e => e.InTransaction && !e.DependentsRemoved));
        Assert.AreEqual(0, f.Versions.Count);
        Assert.AreEqual(0, f.ReleaseCalls);
        Assert.IsTrue(f.PageSizes.All(n => n <= 128));
        Assert.IsTrue(f.PageSizes.Count >= 2);
        CollectionAssert.AreEquivalent(ids, f.Events.Select(e => ((DocumentVersionDeletedIntegrationEvent)e.Payload).VersionId).ToArray());
    }

    [TestMethod]
    public async Task Purge_second_event_failure_rolls_back_entire_batch()
    {
        using var f = new Fixture { FailEventAt = 2 };
        await Assert.ThrowsExactlyAsync<IOException>(() => f.DeleteAsync("purge"));
        Assert.AreEqual(3, f.Versions.Count);
        Assert.IsFalse(f.DependentsRemoved);
        Assert.AreEqual(0, f.Events.Count);
    }

    internal sealed class Fixture : IQueryExecutor, ICommandExecutor, ICommandTransaction, IOutboxWriter, IDisposable
    {
        internal readonly Guid Item = Guid.CreateVersion7();
        internal readonly Guid File = Guid.CreateVersion7();
        internal readonly List<DocumentVersionRecord> Versions = [];
        internal readonly List<(object Payload, bool InTransaction, bool DependentsRemoved)> Events = [];
        internal readonly List<int> PageSizes = [];
        internal Guid Target { get; }
        internal int FailEventAt { get; init; }
        internal int ReleaseCalls;
        internal int Audits;
        internal bool DependentsRemoved;
        private bool inTransaction;
        private readonly ServiceProvider services;

        internal Fixture(DatabaseProvider provider = DatabaseProvider.SqlServer, int count = 3)
        {
            for (var n = 1; n <= count; n++) Versions.Add(new DocumentVersionRecord
            {
                Id = Guid.CreateVersion7(), DocumentItemId = Item, FileId = File, VersionNumber = n,
                CreatedAtUtc = DateTimeOffset.UtcNow, UploadedByUserId = Item,
            });
            Target = Versions[0].Id;
            var claims = Substitute.For<IHostFileReferenceClaimService>();
            claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                ReleaseCalls++;
                return Task.FromResult(Result<bool>.Failure(new Error("files.unavailable", "offline", ErrorType.Conflict)));
            });
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            var retention = Substitute.For<IOptionsMonitor<DocumentVersionRetentionOptions>>();
            retention.CurrentValue.Returns(new DocumentVersionRetentionOptions { MinimumRetainedVersionsPerItem = 2 });
            services = new ServiceCollection().AddSingleton<IQueryExecutor>(this)
                .AddSingleton<ICommandExecutor>(this).AddSingleton<ICommandTransaction>(this)
                .AddSingleton<IOutboxWriter>(this).AddSingleton(claims).AddSingleton(clock).AddSingleton(ids)
                .AddSingleton(retention).AddSingleton(Options.Create(new DatabaseOptions { Provider = provider }))
                // Purge 不进入恢复或内容读取；保留实际协调器类型，以便构造签名变化由 DI 自动覆盖。
                .AddSingleton(new HostDocumentItemQueryService(null!, this, null!, null!, null!,
                    Options.Create(new DatabaseOptions { Provider = provider })))
                .AddSingleton(new DocumentItemRestoreService(this, this, null!, clock, null!))
                .AddSingleton<DocumentItemWriteGuard>().BuildServiceProvider();
        }

        internal async Task<bool> DeleteAsync(string mode)
        {
            if (mode == "purge")
            {
                var recycle = ActivatorUtilities.CreateInstance<HostRecycleBinManagementService>(services);
                return (await recycle.PurgeAsync(Item)).IsSuccess;
            }
            var deletion = ActivatorUtilities.CreateInstance<DocumentVersionDeletionService>(services);
            return mode == "manual"
                ? (await deletion.DeleteVersionManuallyAsync(Item, Target, Item, new(1))).IsSuccess
                : await deletion.TryDeleteVersionForRetentionAsync(Item, Target);
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            object value = typeof(T) == typeof(int) ? statement == DocumentVersionRetentionSql.CountVersionsByItemId ? Versions.Count : 1
                : typeof(T) == typeof(DocumentVersionRecord) ? Versions[0]
                : new DocumentItemDetailRecord { Id = Item, Title = "item", CurrentVersionId = Versions[^1].Id,
                    Version = 1, FileId = File, VersionId = Versions[^1].Id, VersionNumber = Versions[^1].VersionNumber,
                    VersionCreatedAtUtc = DateTimeOffset.UtcNow, UploadedByUserId = Item, SizeBytes = 8 };
            return Task.FromResult((T?)value);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var p = (Dictionary<string, object?>)parameters!;
            var after = p.TryGetValue("AfterId", out var id) ? (Guid?)id : null;
            var size = p.TryGetValue("BatchSize", out var n) ? (int)n! : int.MaxValue;
            var rows = Versions.OrderBy(v => v.Id).Where(v => after is null || v.Id.CompareTo(after.Value) > 0).Take(size).ToArray();
            PageSizes.Add(rows.Length);
            return Task.FromResult<IReadOnlyList<T>>(rows.Cast<T>().ToArray());
        }

        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement == DocumentVersionRetentionSql.InsertDeletionAudit) Audits++;
            if (statement == DocumentVersionRetentionSql.DeleteVersionById) Versions.RemoveAt(0);
            if (statement == DocumentItemSql.PurgeDependents) { DependentsRemoved = true; Versions.Clear(); }
            return Task.FromResult(1);
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            var versions = Versions.ToArray(); var audits = Audits; var count = Events.Count; var removed = DependentsRemoved;
            inTransaction = true;
            try { return await action(cancellationToken); }
            catch { Versions.Clear(); Versions.AddRange(versions); Audits = audits; Events.RemoveRange(count, Events.Count - count); DependentsRemoved = removed; throw; }
            finally { inTransaction = false; }
        }

        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType,
            int schemaVersion, TEvent payload, CancellationToken cancellationToken = default)
        {
            if (FailEventAt == Events.Count + 1) throw new IOException("outbox offline");
            Events.Add((payload!, inTransaction, DependentsRemoved));
            return Task.CompletedTask;
        }

        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType,
            int schemaVersion, TEvent payload, IntegrationEventMetadata metadata, CancellationToken cancellationToken = default) =>
            AddAsync(eventType, schemaVersion, payload, cancellationToken);

        public void Dispose() => services.Dispose();
    }
}
