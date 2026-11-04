using System.Data.Common;
using System.Runtime.ExceptionServices;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实双库验证退休代际、旧键保留及探测后刷新和同文件重绑的安全边界。</summary>
internal static class MediaActiveClaimAssertions
{
    internal static async Task VerifyAsync(IServiceProvider services, DbConnection db, DatabaseProvider provider,
        string kind, Guid subject, Guid file, string currentKey, CancellationToken ct)
    {
        var owner = kind == "logo" ? "tenancy" : "identity";
        var claims = services.GetRequiredService<IHostFileReferenceClaimService>();
        var probe = (IHostImmutableFileBindingClaimProbe)services.GetServices<IHostFileReferenceClaimProbe>().Single(p => p.ConsumerModule == owner);
        var orphan = Key(kind, subject, file, Guid.CreateVersion7()); var legacy = Key(kind, subject, file, null);
        await AddAsync(orphan); await AddAsync(legacy);
        await AgeAsync([orphan, legacy, currentKey]);
        await Runner([probe]).RunOnceAsync(new() { MaxBatchesPerRun = 1 }, ct);
        Assert.AreEqual("released", await StateAsync(orphan)); Assert.AreEqual("active", await StateAsync(currentKey));
        Assert.AreEqual("active", await StateAsync(legacy), "历史双参数键不得凭可复用主体的当前状态自动清理。");

        var refreshed = Key(kind, subject, file, Guid.CreateVersion7()); await AddAsync(refreshed); await AgeAsync([refreshed]);
        await GatedAsync(refreshed, async () =>
        {
            object now = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
            await db.ExecuteAsync(new CommandDefinition("UPDATE fn_files_file_reference_claim SET UpdatedAtUtc = @Now WHERE IdempotencyKey = @Key AND State = 'active'",
                new { Now = now, Key = refreshed }, cancellationToken: ct));
        });
        Assert.AreEqual("active", await StateAsync(refreshed), "探测后被刷新的扫描快照不得释放。");

        var retired = Key(kind, subject, file, Guid.CreateVersion7()); var rebound = Key(kind, subject, file, Guid.CreateVersion7());
        await AddAsync(retired); await AgeAsync([retired]);
        await GatedAsync(retired, async () =>
        {
            await AddAsync(rebound);
            var table = kind == "logo" ? "fn_tenancy_tenant" : "fn_identity_user_profile";
            var idColumn = kind == "logo" ? "Id" : "UserId";
            var prefix = kind switch { "avatar" => "Avatar", "signature" => "Signature", "logo" => "Logo", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
            // 自有夹具在权威探测完成后提交另一代际；不能把旧代际恢复为当前绑定。
            await db.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {prefix}FileId = @File, {prefix}ClaimKey = @Key, Version = Version + 1 WHERE {idColumn} = @Id",
                new { File = file, Key = rebound, Id = subject }, cancellationToken: ct));
        });
        Assert.AreEqual("released", await StateAsync(retired)); Assert.AreEqual("active", await StateAsync(rebound));
        Assert.AreEqual("active", await StateAsync(legacy)); Assert.AreEqual("active", await StateAsync(refreshed));

        async Task AddAsync(string key)
        {
            Assert.IsTrue((await claims.ClaimAsync(new(key, owner, subject, file), ct)).IsSuccess);
            Assert.IsTrue((await claims.ConfirmAsync(key, ct)).IsSuccess);
        }
        Task<string> StateAsync(string key) => db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT State FROM fn_files_file_reference_claim WHERE IdempotencyKey = @Key", new { Key = key }, cancellationToken: ct));
        async Task AgeAsync(string[] keys)
        {
            object old = provider == DatabaseProvider.MySql ? DateTime.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow.AddHours(-2);
            await db.ExecuteAsync(new CommandDefinition("UPDATE fn_files_file_reference_claim SET CreatedAtUtc = @Old, UpdatedAtUtc = @Old, ConfirmedAtUtc = @Old WHERE IdempotencyKey IN @Keys",
                new { Old = old, Keys = keys }, cancellationToken: ct));
        }
        ActiveHostFileReferenceClaimReconciliationRunner Runner(IEnumerable<IHostFileReferenceClaimProbe> probes) => new(
            services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(), probes,
            services.GetRequiredService<IOptions<DatabaseOptions>>(), services.GetRequiredService<IClock>());
        async Task GatedAsync(string key, Func<Task> mutation)
        {
            var gate = new Gate(probe, key); using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = Runner([gate]).RunOnceAsync(new() { MaxBatchesPerRun = 1 }, cancelled.Token);
            Exception? error = null;
            try { await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); await mutation(); }
            catch (Exception failure) { error = failure; }
            finally
            {
                if (error is not null) cancelled.Cancel(); gate.Continue.TrySetResult();
                try { await pending; } catch (Exception failure) { error ??= failure; }
            }
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private sealed class Gate(IHostImmutableFileBindingClaimProbe inner, string key) : IHostImmutableFileBindingClaimProbe
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ConsumerModule => inner.ConsumerModule;
        public Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(Guid reference, Guid file, CancellationToken ct = default) => inner.ProbeReferenceAsync(reference, file, ct);
        public Task<HostFileReferenceClaimProbeResult> ProbeBindingAsync(Guid reference, Guid file, string binding, CancellationToken ct = default) => inner.ProbeBindingAsync(reference, file, binding, ct);
        public async Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeBindingsAsync(IReadOnlyList<HostFileBindingClaimProbeRequest> requests, CancellationToken ct = default)
        {
            var results = await inner.ProbeBindingsAsync(requests, ct);
            if (requests.Any(r => r.IdempotencyKey == key)) { Ready.TrySetResult(); await Continue.Task.WaitAsync(ct); }
            return results;
        }
    }

    private static string Key(string kind, Guid subject, Guid file, Guid? binding) => (kind, binding) switch
    {
        ("avatar", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, id),
        ("signature", Guid id) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, id),
        ("logo", Guid id) => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, id),
        ("avatar", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file),
        ("signature", _) => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file),
    };
}
