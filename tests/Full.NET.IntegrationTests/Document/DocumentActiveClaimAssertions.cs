using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Document;

/// <summary>在自有数据库模拟丢失历史删除事实，验证不可复用引用补偿与真实更新竞争。</summary>
internal static class DocumentActiveClaimAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, CancellationToken ct)
    {
        await using var db = DocumentReleaseTestConnections.Create(factory.Provider, factory.ConnectionString);
        await db.OpenAsync(ct); var seed = await DocumentActiveClaimSeed.CreateAsync(db, factory.Provider, ct);
        var cursor = new ActiveHostFileReferenceClaimReconciliationCursor();
        var options = new ActiveHostFileReferenceClaimReconciliationOptions { BatchSize = 1, MaxBatchesPerRun = 1 };
        await using var scope = factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider;
        var host = p.GetRequiredService<Full.NET.Abstractions.Tenancy.ICurrentTenantContextWriter>(); host.SetHost();
        try
        {
            var probes = p.GetServices<IHostFileReferenceClaimProbe>().ToArray();
            ActiveHostFileReferenceClaimReconciliationRunner Runner(IEnumerable<IHostFileReferenceClaimProbe> owners) =>
                new(p.GetRequiredService<IQueryExecutor>(), p.GetRequiredService<ICommandExecutor>(), owners,
                    p.GetRequiredService<IOptions<DatabaseOptions>>(), p.GetRequiredService<IClock>(), cursor);
            Assert.AreEqual(0, (await Runner(probes).RunOnceAsync(options, ct)).Released, "Soft-deleted document still owns its retained version.");
            Assert.AreEqual(1, (await Runner(probes).RunOnceAsync(options, ct)).Released, "A later orphan must not starve behind a retained first page.");
            Assert.AreEqual("released", await seed.StateAsync(db, seed.OrphanClaim, ct));
            foreach (var id in new[] { seed.RetainedClaim, seed.MutableClaim, seed.FreshClaim })
                Assert.AreEqual("active", await seed.StateAsync(db, id, ct));

            var race = await seed.AddMissingAsync(db, factory.Provider, "document", seed.OldUtc.AddMinutes(3), ct);
            var gate = new ProbeGate(probes.OfType<IHostImmutableFileReferenceClaimProbe>().Single(x => x.ConsumerModule == "document"));
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = Runner([gate]).RunOnceAsync(options, runCancellation.Token);
            Exception? primaryError = null;
            ActiveHostFileReferenceClaimReconciliationResult? raceResult = null;
            try
            {
                await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                object now = factory.Provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
                await db.ExecuteAsync(new CommandDefinition(
                    "UPDATE fn_files_file_reference_claim SET UpdatedAtUtc = @Now WHERE Id = @Id AND State = 'active'",
                    new { Id = race, Now = now }, cancellationToken: ct));
            }
            catch (Exception error) { primaryError = error; }
            finally
            {
                if (primaryError is not null) runCancellation.Cancel();
                gate.Continue.TrySetResult();
                try { raceResult = await pending; }
                catch (Exception error) { primaryError ??= error; }
            }
            if (primaryError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryError).Throw();
            Assert.IsNotNull(raceResult);
            Assert.AreEqual(0, raceResult.Released, "The old NotFound must not overwrite the refreshed claim.");
            Assert.AreEqual("active", await seed.StateAsync(db, race, ct));
        }
        finally { host.Clear(); }
    }

    private sealed class ProbeGate(IHostImmutableFileReferenceClaimProbe inner) : IHostImmutableFileReferenceClaimProbe
    {
        public string ConsumerModule => inner.ConsumerModule;
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(Guid reference, Guid file, CancellationToken ct = default) =>
            inner.ProbeReferenceAsync(reference, file, ct);
        public async Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeReferencesAsync(
            IReadOnlyList<HostFileReferenceClaimProbeRequest> requests, CancellationToken ct = default)
        {
            var result = await inner.ProbeReferencesAsync(requests, ct);
            Ready.TrySetResult(); await Continue.Task.WaitAsync(ct); return result;
        }
    }
}

/// <summary>JIT 与原生 Worker 共用同一场景数据；只修改本测试创建的 UUID 行。</summary>
internal sealed class DocumentActiveClaimSeed
{
    internal Guid File { get; } = Guid.CreateVersion7();
    internal Guid RetainedClaim { get; private set; }
    internal Guid OrphanClaim { get; private set; }
    internal Guid MutableClaim { get; private set; }
    internal Guid FreshClaim { get; private set; }
    internal DateTimeOffset OldUtc { get; } = DateTimeOffset.UtcNow.AddHours(-2);

    internal static async Task<DocumentActiveClaimSeed> CreateAsync(System.Data.Common.DbConnection db, DatabaseProvider provider, CancellationToken ct)
    {
        var seed = new DocumentActiveClaimSeed(); var item = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        object old = provider == DatabaseProvider.MySql ? seed.OldUtc.UtcDateTime : seed.OldUtc;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
            VALUES (@File, NULL, 'active-claim-test.bin', 'application/octet-stream', 1, 'local', @Key, 'ready', @Old, @Actor)
            """, new { seed.File, Key = "active-claim/" + seed.File.ToString("N"), Old = old, Actor = actor }, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_document_item (Id, TenantId, Title, IsDeleted, DeletedAtUtc, DeletedByUserId, CreatedAtUtc, CreatedByUserId)
            VALUES (@Item, NULL, @Title, 1, @Old, @Actor, @Old, @Actor)
            """, new { Item = item, Title = "active-claim-" + item.ToString("N"), Old = old, Actor = actor }, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_document_version (Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes, UploadedByUserId, CreatedAtUtc)
            VALUES (@Version, @Item, @File, 1, NULL, 1, @Actor, @Old)
            """, new { Version = version, Item = item, seed.File, Actor = actor, Old = old }, cancellationToken: ct));
        seed.RetainedClaim = await seed.InsertAsync(db, provider, "document", version, seed.OldUtc, ct);
        seed.OrphanClaim = await seed.AddMissingAsync(db, provider, "document", seed.OldUtc.AddMinutes(1), ct);
        seed.MutableClaim = await seed.AddMissingAsync(db, provider, "identity", seed.OldUtc.AddMinutes(2), ct);
        seed.FreshClaim = await seed.AddMissingAsync(db, provider, "document", DateTimeOffset.UtcNow, ct);
        return seed;
    }

    internal Task<Guid> AddMissingAsync(System.Data.Common.DbConnection db, DatabaseProvider provider, string module, DateTimeOffset updated, CancellationToken ct) =>
        InsertAsync(db, provider, module, Guid.CreateVersion7(), updated, ct);

    private async Task<Guid> InsertAsync(System.Data.Common.DbConnection db, DatabaseProvider provider, string module, Guid reference, DateTimeOffset updated, CancellationToken ct)
    {
        var id = Guid.CreateVersion7(); object time = provider == DatabaseProvider.MySql ? updated.UtcDateTime : updated;
        object old = provider == DatabaseProvider.MySql ? OldUtc.UtcDateTime : OldUtc;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file_reference_claim (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc, ConfirmedAtUtc)
            VALUES (@Id, @Key, @File, @Module, @Reference, 'active', NULL, 1, @Old, @Time, @Old)
            """, new { Id = id, Key = "active-claim-test:" + reference.ToString("D"), File, Module = module, Reference = reference, Old = old, Time = time }, cancellationToken: ct));
        return id;
    }

    internal Task<string> StateAsync(System.Data.Common.DbConnection db, Guid id, CancellationToken ct) => db.QuerySingleAsync<string>(
        new CommandDefinition("SELECT State FROM fn_files_file_reference_claim WHERE Id = @Id", new { Id = id }, cancellationToken: ct));
}
