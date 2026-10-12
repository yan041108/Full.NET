using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>历史 Active 引用补偿结果；只记录实际条件更新成功的释放。</summary>
internal sealed record ActiveHostFileReferenceClaimReconciliationResult(int Scanned, int Released, int ProbeFailures, int BatchesExecuted)
{
    public static ActiveHostFileReferenceClaimReconciliationResult Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>仅向不可复用引用所有者探测；跨模块读取在 Files 条件更新之前，不共享本地事务。</summary>
internal sealed class ActiveHostFileReferenceClaimReconciliationRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IEnumerable<IHostFileReferenceClaimProbe> probes,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    ActiveHostFileReferenceClaimReconciliationCursor? cursor = null)
{
    private readonly ActiveHostFileReferenceClaimReconciliationCursor _cursor = cursor ?? new();
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;
    private readonly IReadOnlyDictionary<string, IHostImmutableFileReferenceClaimProbe> _probes =
        probes.OfType<IHostImmutableFileReferenceClaimProbe>().ToDictionary(p => p.ConsumerModule, StringComparer.Ordinal);

    public async Task<ActiveHostFileReferenceClaimReconciliationResult> RunOnceAsync(
        ActiveHostFileReferenceClaimReconciliationOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || _probes.Count == 0) return ActiveHostFileReferenceClaimReconciliationResult.Empty;
        var validation = new ActiveHostFileReferenceClaimReconciliationOptionsValidator().Validate(null, options);
        if (validation.Failed)
            throw new OptionsValidationException(ActiveHostFileReferenceClaimReconciliationOptions.SectionName, options.GetType(), validation.Failures);
        await _cursor.Gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await RunCoreAsync(options, ct).ConfigureAwait(false); }
        finally { _cursor.Gate.Release(); }
    }

    private async Task<ActiveHostFileReferenceClaimReconciliationResult> RunCoreAsync(
        ActiveHostFileReferenceClaimReconciliationOptions options, CancellationToken ct)
    {
        var currentCutoff = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        // 一次完整扫描固定上界；持续新增引用不会无限延长周期，收紧保护窗口则采用更早的上界。
        _cursor.StaleBeforeUtc = _cursor.StaleBeforeUtc is { } previous && previous < currentCutoff ? previous : currentCutoff;
        var scanned = 0; var released = 0; var failures = 0; var batches = 0;
        var statement = _provider switch
        {
            DatabaseProvider.SqlServer => HostFileReferenceClaimSql.SelectStaleActiveSqlServer,
            DatabaseProvider.MySql => HostFileReferenceClaimSql.SelectStaleActiveMySql,
            _ => throw new NotSupportedException($"Unsupported database provider '{_provider}'."),
        };
        while (batches < options.MaxBatchesPerRun)
        {
            ct.ThrowIfCancellationRequested();
            var rows = await queryExecutor.QueryAsync<HostFileReferenceClaimRecord>(statement,
                new Dictionary<string, object?>
                {
                    ["ActiveState"] = HostFileReferenceClaimStates.Active, ["ConsumerModules"] = _probes.Keys.ToArray(),
                    ["StaleBeforeUtc"] = _cursor.StaleBeforeUtc, ["HasCursor"] = _cursor.Id.HasValue ? 1 : 0,
                    ["AfterUpdatedAtUtc"] = _cursor.UpdatedAtUtc, ["AfterId"] = _cursor.Id, ["BatchSize"] = options.BatchSize,
                }, ct).ConfigureAwait(false);
            batches++;
            if (rows.Count == 0) { _cursor.Reset(); break; }
            scanned += rows.Count;
            var valid = rows.Where(row => row.Id != Guid.Empty && row.FileId != Guid.Empty && row.ConsumerReferenceId != Guid.Empty
                && row.State == HostFileReferenceClaimStates.Active && _probes.ContainsKey(row.ConsumerModule)).ToArray();
            failures += rows.Count - valid.Length;
            foreach (var group in valid.GroupBy(row => row.ConsumerModule, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult> answers;
                var batch = group.ToArray();
                try
                {
                    answers = await _probes[group.Key].ProbeReferencesAsync(batch.Select(row =>
                        new HostFileReferenceClaimProbeRequest(row.Id, row.ConsumerReferenceId, row.FileId)).ToArray(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { failures += batch.Length; continue; }
                ct.ThrowIfCancellationRequested();
                foreach (var row in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    if (answers is null || !answers.TryGetValue(row.Id, out var result)) { failures++; continue; }
                    if (result?.Outcome == HostFileReferenceClaimProbeOutcome.Exists) continue;
                    if (result?.Outcome != HostFileReferenceClaimProbeOutcome.NotFound) { failures++; continue; }
                    var affected = await commandExecutor.ExecuteAsync(HostFileReferenceClaimSql.ReleaseStaleActive,
                        new Dictionary<string, object?>
                        {
                            ["Id"] = row.Id, ["ActiveState"] = HostFileReferenceClaimStates.Active,
                            ["ObservedUpdatedAtUtc"] = row.UpdatedAtUtc, ["ObservedFileId"] = row.FileId,
                            ["ObservedConsumerModule"] = row.ConsumerModule, ["ObservedConsumerReferenceId"] = row.ConsumerReferenceId,
                            ["ReleasedState"] = HostFileReferenceClaimStates.Released, ["Now"] = clock.UtcNow,
                        }, ct).ConfigureAwait(false);
                    if (affected == 1) released++;
                }
            }
            _cursor.UpdatedAtUtc = rows[^1].UpdatedAtUtc; _cursor.Id = rows[^1].Id;
            if (rows.Count < options.BatchSize) { _cursor.Reset(); break; }
        }
        return new(scanned, released, failures, batches);
    }
}
