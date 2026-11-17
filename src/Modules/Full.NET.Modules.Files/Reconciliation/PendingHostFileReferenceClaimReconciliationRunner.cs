using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Extensions.Options;

/// <summary>后台对账或补偿执行器；单飞与租约由调用方保证。</summary>
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
    HostFileScanCursor<PendingHostFileReferenceClaimReconciliationRunner>? cursor = null)
{
    private readonly HostFileScanCursor<PendingHostFileReferenceClaimReconciliationRunner> _cursor = cursor ?? new();
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;
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
        {
            throw new OptionsValidationException(
                PendingHostFileReferenceClaimReconciliationOptions.SectionName,
                typeof(PendingHostFileReferenceClaimReconciliationOptions),
                validation.Failures);
        }

        await _cursor.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cursor.Gate.Release();
        }
    }

    private async Task<PendingHostFileReferenceClaimReconciliationResult> RunCoreAsync(
        PendingHostFileReferenceClaimReconciliationOptions options, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var staleCutoff = now.AddSeconds(-options.MinimumAgeSeconds);
        var releaseCutoff = now.AddSeconds(-options.ReleaseGraceSeconds);
        // 当前环扫的候选与释放窗口都可立即收紧，放宽从下一轮生效。
        var staleBeforeUtc = _cursor.CutoffUtc is { } previousStale && previousStale < staleCutoff
            ? previousStale
            : staleCutoff;
        var releaseBeforeUtc = _cursor.ReleaseBeforeUtc is { } previousRelease && previousRelease < releaseCutoff
            ? previousRelease
            : releaseCutoff;
        _cursor.CutoffUtc = staleBeforeUtc;
        _cursor.ReleaseBeforeUtc = releaseBeforeUtc;
        var scanned = 0;
        var promoted = 0;
        var released = 0;
        var probeFailures = 0;
        var batches = 0;
        var hasCursor = _cursor.Id is not null;
        var afterUpdatedAtUtc = _cursor.Timestamp;
        Guid? afterId = _cursor.Id;

        while (batches < options.MaxBatchesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await queryExecutor.QueryAsync<HostFileReferenceClaimRecord>(
                    SelectStatement(),
                    new Dictionary<string, object?>
                    {
                        ["PendingState"] = HostFileReferenceClaimStates.Pending,
                        ["StaleBeforeUtc"] = staleBeforeUtc,
                        ["HasCursor"] = hasCursor ? 1 : 0,
                        ["AfterUpdatedAtUtc"] = afterUpdatedAtUtc,
                        ["AfterId"] = afterId,
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
                    // 可复用引用必须匹配扫描到的绑定键，避免同一文件的其他绑定代际误确认。
                    probeResult = probe is IHostFileReferenceBindingClaimProbe bindingProbe
                        ? await bindingProbe.ProbeBindingAsync(record.ConsumerReferenceId, record.FileId,
                            record.IdempotencyKey, cancellationToken).ConfigureAwait(false)
                        : await probe.ProbeReferenceAsync(record.ConsumerReferenceId, record.FileId,
                            cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    probeFailures++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (probeResult is null)
                {
                    probeFailures++;
                    continue;
                }

                if (probeResult.Outcome == HostFileReferenceClaimProbeOutcome.Exists)
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

                if (probeResult.Outcome != HostFileReferenceClaimProbeOutcome.NotFound)
                {
                    probeFailures++;
                    continue;
                }

                if (record.CreatedAtUtc > releaseBeforeUtc)
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

            cancellationToken.ThrowIfCancellationRequested();
            var last = records[^1];
            hasCursor = true;
            afterUpdatedAtUtc = last.UpdatedAtUtc;
            afterId = last.Id;
            _cursor.Timestamp = afterUpdatedAtUtc;
            _cursor.Id = afterId;
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
