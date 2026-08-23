using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

/// <summary>
/// Dapper 实现的 Seed 编排器：展开 Profile 继承链、按依赖图排序 Contributor、获取执行租约并写入幂等审计。
/// </summary>
/// <remarks>
/// <para>本编排器按 <see cref="SeedProfileNames.EffectiveLayers"/> 展开目标 Profile 的确定性继承层，
/// 通过 <see cref="SeedContributorGraph"/> 拓扑排序 Contributor 后逐个执行；
/// Production 环境直接拒绝除 <see cref="SeedProfile.Baseline"/> 之外的 Profile，避免开发/演示数据进入生产。</para>
/// <para>同一数据库同一时刻只允许一个 Seed Run 在执行，由 <see cref="ISeedExecutionLeaseProvider"/> 串行化；
/// 任一 Contributor 抛出受控异常时立即停止后续执行，并在审计表中标记当前项与整次 Run 的稳定错误码，
/// 不进行自动重试或回滚，由调用方依据审计结果决定处置。</para>
/// <para>每次执行产生新的 <see cref="SeedContext.RunId"/>，审计记录承载关联标识（优先取当前 Activity TraceId），
/// 日志与审计均禁止包含 Secret、连接串或异常堆栈。</para>
/// </remarks>
internal sealed class SeedOrchestrator(
    IEnumerable<IDataSeedContributor> contributors,
    ISeedExecutionLeaseProvider leaseProvider,
    ISeedExecutionStore store,
    IHostEnvironment environment,
    IOptions<SeedOptions> options,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<SeedOrchestrator> logger) : ISeedOrchestrator
{
    private readonly IReadOnlyCollection<IDataSeedContributor> _contributors =
        contributors.ToArray();
    private readonly SeedOptions _options = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// 实现按继承链展开 Profile、获取租约后委托 <see cref="ExecuteAsync"/>；并发请求由租约串行化，
    /// 重复请求返回 <see cref="SeedErrorCodes"/> 中定义的租约占用错误而非并行执行。
    /// </remarks>
    public async Task<Result<SeedRunResult>> RunAsync(
        SeedProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (environment.IsProduction() && profile != SeedProfile.Baseline)
        {
            return Failure(SeedErrorCodes.ProfileNotAllowed, ErrorType.Forbidden);
        }

        IReadOnlyList<IDataSeedContributor> ordered;
        try
        {
            ordered = SeedContributorGraph.Order(_contributors, profile);
        }
        catch (SeedConfigurationException exception)
        {
            return Failure(exception.Code, ErrorType.Validation);
        }

        IAsyncDisposable lease;
        try
        {
            lease = await leaseProvider.AcquireAsync(cancellationToken);
        }
        catch (SeedExecutionException exception)
        {
            return Failure(exception.Code, ErrorType.Conflict);
        }
        catch (OperationCanceledException)
        {
            return Failure(SeedErrorCodes.ExecutionCancelled, ErrorType.BusinessRule);
        }

        await using (lease)
        {
            return await ExecuteAsync(ordered, profile, cancellationToken);
        }
    }

    private async Task<Result<SeedRunResult>> ExecuteAsync(
        IReadOnlyList<IDataSeedContributor> ordered,
        SeedProfile profile,
        CancellationToken cancellationToken)
    {
        var runId = idGenerator.NewId();
        var correlationId = Activity.Current?.TraceId.ToString() ?? runId.ToString("N");
        var context = new SeedContext(
            runId,
            profile,
            environment.EnvironmentName,
            _options.DefaultLocale,
            correlationId);
        await store.StartRunAsync(
            new SeedRunAuditStart(
                runId,
                profile.ToCanonicalName(),
                environment.EnvironmentName,
                GetApplicationVersion(),
                correlationId,
                clock.UtcNow),
            cancellationToken);

        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        IDataSeedContributor? currentContributor = null;
        try
        {
            foreach (var contributor in ordered)
            {
                currentContributor = contributor;
                await store.StartItemAsync(
                    new SeedRunItemAuditStart(
                        runId,
                        contributor.Name,
                        contributor.Version,
                        clock.UtcNow),
                    cancellationToken);
                var contribution = await contributor.SeedAsync(context, cancellationToken);
                createdCount += contribution.CreatedCount;
                updatedCount += contribution.UpdatedCount;
                skippedCount += contribution.SkippedCount;
                await store.CompleteItemAsync(
                    CompleteItem(
                        runId,
                        contributor.Name,
                        SeedExecutionStatuses.Succeeded,
                        contribution,
                        null),
                    cancellationToken);
                currentContributor = null;
            }

            await store.CompleteRunAsync(
                CompleteRun(runId, SeedExecutionStatuses.Succeeded, null),
                cancellationToken);
            return Result<SeedRunResult>.Success(new SeedRunResult(
                runId,
                profile,
                ordered.Count,
                createdCount,
                updatedCount,
                skippedCount));
        }
        catch (OperationCanceledException)
        {
            if (currentContributor is not null)
            {
                await store.CompleteItemAsync(
                    CompleteItem(
                        runId,
                        currentContributor.Name,
                        SeedExecutionStatuses.Cancelled,
                        null,
                        SeedErrorCodes.ExecutionCancelled),
                    CancellationToken.None);
            }

            await store.CompleteRunAsync(
                CompleteRun(
                    runId,
                    SeedExecutionStatuses.Cancelled,
                    SeedErrorCodes.ExecutionCancelled),
                CancellationToken.None);
            return Failure(SeedErrorCodes.ExecutionCancelled, ErrorType.BusinessRule);
        }
        catch (SeedContributionException exception)
        {
            if (currentContributor is not null)
            {
                await store.CompleteItemAsync(
                    CompleteItem(
                        runId,
                        currentContributor.Name,
                        SeedExecutionStatuses.Failed,
                        null,
                        exception.Code),
                    CancellationToken.None);
            }

            await store.CompleteRunAsync(
                CompleteRun(runId, SeedExecutionStatuses.Failed, exception.Code),
                CancellationToken.None);
            return Failure(exception.Code, ErrorType.Conflict);
        }
        catch (Exception exception)
        {
            if (currentContributor is not null)
            {
                await store.CompleteItemAsync(
                    CompleteItem(
                        runId,
                        currentContributor.Name,
                        SeedExecutionStatuses.Failed,
                        null,
                        SeedErrorCodes.ContributorFailed),
                    CancellationToken.None);
            }

            await store.CompleteRunAsync(
                CompleteRun(
                    runId,
                    SeedExecutionStatuses.Failed,
                    SeedErrorCodes.ContributorFailed),
                CancellationToken.None);
            logger.LogError(
                exception,
                "Seed contributor {ContributorName} failed with {ErrorCode}",
                currentContributor?.Name ?? "unknown",
                SeedErrorCodes.ContributorFailed);
            return Failure(SeedErrorCodes.ContributorFailed, ErrorType.Unexpected);
        }
    }

    private SeedRunItemAuditCompletion CompleteItem(
        Guid runId,
        string contributor,
        string status,
        SeedContributionResult? contribution,
        string? errorCode) =>
        new(
            runId,
            contributor,
            status,
            contribution?.CreatedCount ?? 0,
            contribution?.UpdatedCount ?? 0,
            contribution?.SkippedCount ?? 0,
            errorCode,
            clock.UtcNow);

    private SeedRunAuditCompletion CompleteRun(Guid runId, string status, string? errorCode) =>
        new(runId, status, errorCode, clock.UtcNow);

    private static Result<SeedRunResult> Failure(string code, ErrorType type) =>
        Result<SeedRunResult>.Failure(new Error(code, code, type));

    private static string GetApplicationVersion() =>
        typeof(SeedOrchestrator).Assembly.GetName().Version?.ToString() ?? "unknown";
}
