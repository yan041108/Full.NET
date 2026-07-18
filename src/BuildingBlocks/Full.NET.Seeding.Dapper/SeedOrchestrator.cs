using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Seeding.Dapper;

internal sealed class SeedOrchestrator(
    IEnumerable<IDataSeedContributor> contributors,
    ISeedExecutionLeaseProvider leaseProvider,
    ISeedExecutionStore store,
    IHostEnvironment environment,
    IOptions<SeedOptions> options,
    IClock clock,
    IIdGenerator idGenerator) : ISeedOrchestrator
{
    private readonly IReadOnlyCollection<IDataSeedContributor> _contributors =
        contributors.ToArray();
    private readonly SeedOptions _options = options.Value;

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
        catch
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
