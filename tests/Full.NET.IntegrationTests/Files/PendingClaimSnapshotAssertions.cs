using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Reconciliation;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Files;

/// <summary>真实双库验证扫描与释放之间发生确认、刷新时，过期快照不再生效。</summary>
internal static class PendingClaimSnapshotAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try
        {
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var providerKey = scope.ServiceProvider.GetRequiredService<FileStorageProviderRegistry>().DefaultProvider.ProviderKey;
            foreach (var change in new[] { "confirmed", "refreshed", "unchanged" })
            {
                var fileId = Guid.CreateVersion7();
                var claimId = Guid.CreateVersion7();
                var referenceId = Guid.CreateVersion7();
                var key = HostFileReferenceClaimIdempotencyKeys.DocumentVersion(referenceId);
                try
                {
                    Assert.AreEqual(1, await commands.ExecuteAsync(HostFileSql.Insert,
                        HostFileSqlTestParameters.Insert(fileId, null, "claim-snapshot.bin", "application/octet-stream", 1,
                            providerKey, "claim-snapshot/" + fileId.ToString("N"), null, clock.UtcNow.AddHours(-2), Guid.CreateVersion7()), ct));
                    Assert.AreEqual(1, await commands.ExecuteAsync(new SqlStatement("test.files.ready_claim_source",
                        "UPDATE fn_files_file SET StorageState = 'ready' WHERE Id = @Id AND TenantId IS NULL", SqlDataScope.HostOnly),
                        new Dictionary<string, object?> { ["Id"] = fileId }, ct));
                    Assert.AreEqual(1, await commands.ExecuteAsync(HostFileReferenceClaimSql.InsertPending, new Dictionary<string, object?>
                    {
                        ["Id"] = claimId, ["IdempotencyKey"] = key, ["FileId"] = fileId,
                        ["ConsumerModule"] = HostFileReferenceClaimConsumerModules.Document, ["ConsumerReferenceId"] = referenceId,
                        ["State"] = HostFileReferenceClaimStates.Pending, ["ContentHash"] = null, ["SizeBytes"] = 1L,
                        ["CreatedAtUtc"] = clock.UtcNow.AddHours(-2), ["UpdatedAtUtc"] = clock.UtcNow.AddHours(-1),
                    }, ct));

                    var probe = new InterleavingProbe(async (reference, file, token) =>
                    {
                        if (reference != referenceId || file != fileId)
                            return new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.Failed);
                        if (change == "confirmed")
                            Assert.AreEqual(1, await commands.ExecuteAsync(HostFileReferenceClaimSql.ConfirmPending,
                                new Dictionary<string, object?> { ["IdempotencyKey"] = key, ["PendingState"] = HostFileReferenceClaimStates.Pending,
                                    ["ActiveState"] = HostFileReferenceClaimStates.Active, ["Now"] = clock.UtcNow }, token));
                        if (change == "refreshed")
                            Assert.AreEqual(1, await commands.ExecuteAsync(new SqlStatement("test.files.refresh_pending_claim",
                                "UPDATE fn_files_file_reference_claim SET UpdatedAtUtc = @Now WHERE Id = @Id AND State = @State", SqlDataScope.HostOnly),
                                new Dictionary<string, object?> { ["Id"] = claimId, ["State"] = HostFileReferenceClaimStates.Pending, ["Now"] = clock.UtcNow }, token));
                        // 返回先前的不存在结论，强制覆盖扫描与最终写入之间的状态变化。
                        return new HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome.NotFound);
                    });
                    var runner = new PendingHostFileReferenceClaimReconciliationRunner(queries, commands, [probe],
                        Options.Create(new DatabaseOptions { Provider = factory.Provider }), clock);
                    var result = await runner.RunOnceAsync(new PendingHostFileReferenceClaimReconciliationOptions(), ct);
                    Assert.AreEqual(change == "unchanged" ? 1 : 0, result.Released);
                    var after = await queries.QuerySingleOrDefaultAsync<HostFileReferenceClaimRecord>(HostFileReferenceClaimSql.FindByIdempotencyKey,
                        new Dictionary<string, object?> { ["IdempotencyKey"] = key }, ct);
                    Assert.IsNotNull(after);
                    Assert.AreEqual(change == "confirmed" ? HostFileReferenceClaimStates.Active
                        : change == "refreshed" ? HostFileReferenceClaimStates.Pending : HostFileReferenceClaimStates.Released, after.State);
                }
                finally
                {
                    // 只清理本场景生成的唯一文件及其 Claim，不访问其他模块数据或 Blob。
                    try
                    {
                        await commands.ExecuteAsync(new SqlStatement("test.files.remove_snapshot_claim",
                            "DELETE FROM fn_files_file_reference_claim WHERE Id = @Id AND FileId = @FileId", SqlDataScope.HostOnly),
                            new Dictionary<string, object?> { ["Id"] = claimId, ["FileId"] = fileId }, CancellationToken.None);
                    }
                    finally
                    {
                        await commands.ExecuteAsync(new SqlStatement("test.files.remove_snapshot_file",
                            "DELETE FROM fn_files_file WHERE Id = @Id AND TenantId IS NULL", SqlDataScope.HostOnly),
                            new Dictionary<string, object?> { ["Id"] = fileId }, CancellationToken.None);
                    }
                }
            }
        }
        finally { tenant.Clear(); }
    }

    private sealed class InterleavingProbe(Func<Guid, Guid, CancellationToken, Task<HostFileReferenceClaimProbeResult>> probe) : IHostFileReferenceClaimProbe
    {
        public string ConsumerModule => HostFileReferenceClaimConsumerModules.Document;
        public Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(Guid consumerReferenceId, Guid fileId, CancellationToken cancellationToken = default) =>
            probe(consumerReferenceId, fileId, cancellationToken);
    }
}
