using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

internal sealed record PendingHostFileReferenceClaimReconciliationResult(
    int Scanned,
    int Promoted,
    int Released,
    int ProbeFailures,
    int BatchesExecuted)
{
    public static PendingHostFileReferenceClaimReconciliationResult Empty { get; } =
        new(0, 0, 0, 0, 0);
}

internal sealed class PendingHostFileReferenceClaimReconciliationOptions
{
    public const string SectionName = "Files:ReferenceClaimReconciliation";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 10;
    public int MinimumAgeSeconds { get; set; } = 300;
    public int ReleaseGraceSeconds { get; set; } = 900;
    public int PollSeconds { get; set; } = 300;
}

internal sealed class PendingHostFileReferenceClaimReconciliationRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IEnumerable<IHostFileReferenceClaimProbe> probes,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    PendingHostFileReferenceClaimReconciliationCursor? cursor = null)
{
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;
    private readonly PendingHostFileReferenceClaimReconciliationCursor _cursor = cursor ?? new();
    private readonly IReadOnlyDictionary<string, IHostFileReferenceClaimProbe> _probes =
        probes.ToDictionary(probe => probe.ConsumerModule, StringComparer.Ordinal);

    public async Task<PendingHostFileReferenceClaimReconciliationResult> RunOnceAsync(
        PendingHostFileReferenceClaimReconciliationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return PendingHostFileReferenceClaimReconciliationResult.Empty;
        }

        var validation = new PendingHostFileReferenceClaimReconciliationOptionsValidator().Validate(null, options);
        if (validation.Failed)
            throw new OptionsValidationException(PendingHostFileReferenceClaimReconciliationOptions.SectionName, options.GetType(), validation.Failures);
        await _cursor.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false); }
        finally { _cursor.Gate.Release(); }
    }

    private async Task<PendingHostFileReferenceClaimReconciliationResult> RunCoreAsync(
        PendingHostFileReferenceClaimReconciliationOptions options, CancellationToken cancellationToken)
    {
        var stale = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        var release = clock.UtcNow.AddSeconds(-options.ReleaseGraceSeconds);
        // 扫描与释放年龄各自冻结；运行配置收紧不能扩大本周期可释放范围。
        _cursor.StaleBeforeUtc = _cursor.StaleBeforeUtc is { } previous && previous < stale ? previous : stale;
        _cursor.ReleaseBeforeUtc = _cursor.ReleaseBeforeUtc is { } priorRelease && priorRelease < release ? priorRelease : release;
        var scanned = 0;
        var promoted = 0;
        var released = 0;
        var probeFailures = 0;
        var batches = 0;

        while (batches < options.MaxBatchesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await queryExecutor.QueryAsync<HostFileReferenceClaimRecord>(
                    SelectStatement(),
                    new Dictionary<string, object?>
                    {
                        ["PendingState"] = HostFileReferenceClaimStates.Pending,
                        ["StaleBeforeUtc"] = _cursor.StaleBeforeUtc,
                        ["HasCursor"] = _cursor.Id.HasValue ? 1 : 0,
                        ["AfterUpdatedAtUtc"] = _cursor.UpdatedAtUtc,
                        ["AfterId"] = _cursor.Id,
                        ["BatchSize"] = options.BatchSize,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            batches++;
            if (records.Count == 0)
            {
                _cursor.Reset();
                break;
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                if (!_probes.TryGetValue(record.ConsumerModule, out var probe))
                {
                    probeFailures++;
                    continue;
                }

                HostFileReferenceClaimProbeResult probeResult;
                try
                {
                    // 可复用媒体必须精确核对扫描键；其他消费者继续使用原契约，不能猜测代际。
                    probeResult = probe is IHostFileReferenceBindingClaimProbe bindingProbe
                        ? await bindingProbe.ProbeBindingAsync(record.ConsumerReferenceId, record.FileId,
                            record.IdempotencyKey, cancellationToken).ConfigureAwait(false)
                        : await probe.ProbeReferenceAsync(record.ConsumerReferenceId, record.FileId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    probeFailures++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (probeResult?.Outcome == HostFileReferenceClaimProbeOutcome.Exists)
                {
                    var affected = await commandExecutor.ExecuteAsync(
                            HostFileReferenceClaimSql.PromoteToActive,
                            new Dictionary<string, object?>
                            {
                                ["Id"] = record.Id,
                                ["PendingState"] = HostFileReferenceClaimStates.Pending,
                                ["ActiveState"] = HostFileReferenceClaimStates.Active,
                                ["Now"] = clock.UtcNow,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (affected == 1)
                    {
                        promoted++;
                    }

                    continue;
                }

                // 仅显式 NotFound 才可释放；未知枚举不是权威不存在结论。
                if (probeResult?.Outcome != HostFileReferenceClaimProbeOutcome.NotFound)
                {
                    probeFailures++;
                    continue;
                }

                if (record.CreatedAtUtc > _cursor.ReleaseBeforeUtc)
                {
                    continue;
                }

                var releasedRows = await commandExecutor.ExecuteAsync(
                        HostFileReferenceClaimSql.ReleaseStalePending,
                        new Dictionary<string, object?>
                        {
                            ["Id"] = record.Id,
                            ["ObservedUpdatedAtUtc"] = record.UpdatedAtUtc,
                            ["PendingState"] = HostFileReferenceClaimStates.Pending,
                            ["ReleasedState"] = HostFileReferenceClaimStates.Released,
                            ["Now"] = clock.UtcNow,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (releasedRows == 1)
                {
                    released++;
                }
            }

            var last = records[^1];
            cancellationToken.ThrowIfCancellationRequested();
            _cursor.UpdatedAtUtc = last.UpdatedAtUtc;
            _cursor.Id = last.Id;
            if (records.Count < options.BatchSize)
            {
                _cursor.Reset();
                break;
            }
        }

        return new PendingHostFileReferenceClaimReconciliationResult(
            scanned,
            promoted,
            released,
            probeFailures,
            batches);
    }

    private SqlStatement SelectStatement() =>
        _provider switch
        {
            DatabaseProvider.SqlServer => HostFileReferenceClaimSql.SelectStalePendingSqlServer,
            DatabaseProvider.MySql => HostFileReferenceClaimSql.SelectStalePendingMySql,
            _ => throw new NotSupportedException($"Unsupported database provider '{_provider}'."),
        };
}
