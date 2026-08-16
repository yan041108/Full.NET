using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobHealth;

/// <summary>聚合 Handler 注册表、积压快照与 Worker 心跳，供管理端只读观测。</summary>
internal sealed class HostJobHealthQueryService(
    IQueryExecutor queryExecutor,
    JobHandlerRegistry handlerRegistry,
    JobsBacklogReader backlogReader,
    IClock clock,
    IOptions<JobsWorkerOptions> workerOptions)
{
    public async Task<Result<HostJobHealthResponse>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.ToUniversalTime();
        var backlog = await backlogReader.ReadAsync(now, cancellationToken)
            .ConfigureAwait(false);
        var workers = await queryExecutor
            .QueryAsync<JobWorkerInstanceRecord>(
                JobSql.ListWorkerInstances,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var staleThreshold = TimeSpan.FromMilliseconds(
            workerOptions.Value.PollMilliseconds * 2);
        return Result<HostJobHealthResponse>.Success(
            new HostJobHealthResponse(
                handlerRegistry.RegisteredJobKeys,
                new HostJobHealthBacklogSnapshot(
                    backlog.PendingCount,
                    backlog.OldestClaimableCreatedAtUtc,
                    backlog.DueRetryCount,
                    backlog.OldestDueRetryAtUtc),
                workers
                    .Select(worker => new HostJobWorkerInstanceResponse(
                        worker.InstanceId,
                        worker.HostProfile,
                        worker.StartedAtUtc,
                        worker.LastHeartbeatAtUtc,
                        worker.WorkerVersion,
                        now - worker.LastHeartbeatAtUtc > staleThreshold))
                    .ToArray()));
    }
}

internal sealed class JobWorkerInstanceRecord
{
    public Guid InstanceId { get; set; }

    public string HostProfile { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastHeartbeatAtUtc { get; set; }

    public string? WorkerVersion { get; set; }
}
