using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>租户资源文件对账必须提升陈旧 pending、删除缺失对象，并只释放确认无引用的 ready 孤儿。</summary>
[TestClass]
public sealed class PendingTenantResourceFileReconciliationTests
{
    /// <summary>pending 有对象则提升，无对象则删除。</summary>
    [TestMethod]
    public async Task Pending_blob_is_promoted_and_missing_blob_is_purged()
    {
        var tenantId = Guid.NewGuid();
        var existing = NewRecord(tenantId, "pending", "local", "keep");
        var missing = NewRecord(tenantId, "pending", "archive", "gone");
        var store = new FileStore([existing, missing]);
        var runner = CreateRunner(store, tenantId, exists: key => key == "keep");

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(2, result.Scanned);
        Assert.AreEqual(1, result.Promoted);
        Assert.AreEqual(1, result.Purged);
        Assert.AreEqual("ready", store.Files[existing.Id].StatusKey);
        Assert.IsFalse(store.Files.ContainsKey(missing.Id));
    }

    /// <summary>所属模块仍引用的 ready 文件不得释放。</summary>
    [TestMethod]
    public async Task Referenced_ready_file_is_kept()
    {
        var tenantId = Guid.NewGuid();
        var ready = NewRecord(tenantId, "ready", "local", "report");
        var store = new FileStore([ready]);
        var owner = Substitute.For<ITenantResourceFileOwner>();
        owner.OwnerModuleKey.Returns("reporting");
        owner.IsReferencedAsync(ready.ResourceId, ready.Id, Arg.Any<CancellationToken>()).Returns(true);
        var runner = CreateRunner(store, tenantId, exists: _ => true, owner);

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(0, result.Released);
        Assert.AreEqual("ready", store.Files[ready.Id].StatusKey);
    }

    /// <summary>超过宽限期且模块不再引用的 ready 文件必须释放并删除对象。</summary>
    [TestMethod]
    public async Task Unreferenced_ready_orphan_is_released()
    {
        var tenantId = Guid.NewGuid();
        var ready = NewRecord(tenantId, "ready", "local", "orphan");
        var store = new FileStore([ready]);
        var owner = Substitute.For<ITenantResourceFileOwner>();
        owner.OwnerModuleKey.Returns("reporting");
        owner.IsReferencedAsync(ready.ResourceId, ready.Id, Arg.Any<CancellationToken>()).Returns(false);
        var storage = Substitute.For<IFileStorageProvider>();
        storage.ProviderKey.Returns("local");
        var runner = CreateRunner(store, tenantId, exists: _ => true, owner, storage);

        var result = await runner.RunOnceAsync(EnabledOptions(), CancellationToken.None);

        Assert.AreEqual(1, result.Released);
        Assert.AreEqual("released", store.Files[ready.Id].StatusKey);
        await storage.Received(1).DeleteAsync("orphan", Arg.Any<CancellationToken>());
    }

    private static PendingTenantResourceFileReconciliationRunner CreateRunner(
        FileStore store,
        Guid tenantId,
        Func<string, bool> exists,
        ITenantResourceFileOwner? owner = null,
        IFileStorageProvider? localStorage = null)
    {
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantContext(tenantId, "acme", "Acme"));
        localStorage ??= Substitute.For<IFileStorageProvider>();
        localStorage.ProviderKey.Returns("local");
        localStorage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => exists(call.ArgAt<string>(0)));
        var archive = Substitute.For<IFileStorageProvider>();
        archive.ProviderKey.Returns("archive");
        archive.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => exists(call.ArgAt<string>(0)));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        return new PendingTenantResourceFileReconciliationRunner(
            store,
            store,
            new FileStorageProviderRegistry(
                [localStorage, archive],
                Options.Create(new FileStorageOptions { DefaultProviderKey = "local" })),
            owner is null ? [] : [owner],
            resolver,
            new CurrentTenantAccessor(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            clock);
    }

    private static TenantResourceFileReconciliationRecord NewRecord(
        Guid tenantId,
        string statusKey,
        string providerKey,
        string storageKey) =>
        new(
            Guid.NewGuid(),
            tenantId,
            "reporting",
            Guid.NewGuid(),
            providerKey,
            storageKey,
            statusKey,
            DateTimeOffset.UtcNow.AddHours(-1));

    private static PendingTenantResourceFileReconciliationOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            BatchSize = 100,
            MaxBatchesPerRun = 10,
            MinimumAgeSeconds = 30,
            PollSeconds = 300,
        };

    private sealed class FileStore(IReadOnlyList<TenantResourceFileReconciliationRecord> seed)
        : IQueryExecutor, ICommandExecutor
    {
        public Dictionary<Guid, MutableFile> Files { get; } = seed.ToDictionary(
            item => item.Id,
            item => new MutableFile(item));

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            if (statement.Name == TenantResourceFileSql.FindOwned.Name
                && Files.TryGetValue((Guid)values["Id"]!, out var file))
            {
                return Task.FromResult((T?)(object)new TenantResourceFileRecord(
                    file.Id, "name.xlsx", "application/test", 1, "hash", file.ProviderKey, file.StorageKey, file.StatusKey));
            }

            return Task.FromResult(default(T));
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement.Name == TenantResourceFileSql.SelectStaleSqlServer.Name)
            {
                var rows = Files.Values.Select(file => file.ToRecord()).Cast<T>().ToArray();
                return Task.FromResult<IReadOnlyList<T>>(rows);
            }

            return Task.FromResult<IReadOnlyList<T>>([]);
        }

        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = (IReadOnlyDictionary<string, object?>)parameters!;
            var id = (Guid)values["Id"]!;
            if (!Files.TryGetValue(id, out var file))
            {
                return Task.FromResult(0);
            }

            if (statement.Name == TenantResourceFileSql.PromotePending.Name && file.StatusKey == "pending")
            {
                file.StatusKey = "ready";
                return Task.FromResult(1);
            }

            if (statement.Name == TenantResourceFileSql.PurgePending.Name && file.StatusKey == "pending")
            {
                Files.Remove(id);
                return Task.FromResult(1);
            }

            if (statement.Name == TenantResourceFileSql.Release.Name)
            {
                file.StatusKey = "released";
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }

    private sealed class MutableFile(TenantResourceFileReconciliationRecord record)
    {
        public Guid Id { get; } = record.Id;
        public Guid TenantId { get; } = record.TenantId;
        public string OwnerModuleKey { get; } = record.OwnerModuleKey;
        public Guid ResourceId { get; } = record.ResourceId;
        public string ProviderKey { get; } = record.ProviderKey;
        public string StorageKey { get; } = record.StorageKey;
        public string StatusKey { get; set; } = record.StatusKey;
        public DateTimeOffset CreatedAtUtc { get; } = record.CreatedAtUtc;

        public TenantResourceFileReconciliationRecord ToRecord() =>
            new(Id, TenantId, OwnerModuleKey, ResourceId, ProviderKey, StorageKey, StatusKey, CreatedAtUtc);
    }
}
