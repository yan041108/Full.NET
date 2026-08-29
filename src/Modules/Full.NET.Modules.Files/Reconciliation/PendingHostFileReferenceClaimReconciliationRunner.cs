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
    IClock clock)
{
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

        var staleBeforeUtc = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        var releaseBeforeUtc = clock.UtcNow.AddSeconds(-options.ReleaseGraceSeconds);
        var scanned = 0;
        var promoted = 0;
        var released = 0;
        var probeFailures = 0;
        var batches = 0;
        var hasCursor = false;
        var afterUpdatedAtUtc = DateTimeOffset.UnixEpoch;
        Guid? afterId = null;

        while (batches < options.MaxBatchesPerRun)
        {
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
            batches++;
            if (records.Count == 0)
            {
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
                    probeResult = await probe.ProbeReferenceAsync(
                            record.ConsumerReferenceId,
                            record.FileId,
                            cancellationToken)
                        .ConfigureAwait(false);
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

                if (probeResult.Outcome == HostFileReferenceClaimProbeOutcome.Failed)
                {
                    probeFailures++;
                    continue;
                }

                if (record.CreatedAtUtc > releaseBeforeUtc)
                {
                    continue;
                }

                var releasedRows = await commandExecutor.ExecuteAsync(
                        HostFileReferenceClaimSql.ReleaseOpen,
                        new Dictionary<string, object?>
                        {
                            ["IdempotencyKey"] = record.IdempotencyKey,
                            ["PendingState"] = HostFileReferenceClaimStates.Pending,
                            ["ActiveState"] = HostFileReferenceClaimStates.Active,
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
            hasCursor = true;
            afterUpdatedAtUtc = last.UpdatedAtUtc;
            afterId = last.Id;
            if (records.Count < options.BatchSize)
            {
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
