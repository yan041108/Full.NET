using Cronos;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;
using Full.NET.Modules.Jobs.Persistence;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobSchedules;

/// <summary>管理 Host 任务计划，并在写入前固定时区和下一次 UTC 触发时刻。</summary>
internal sealed class HostJobScheduleService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostJobScheduleResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? jobDefinitionId,
        string? search,
        bool? isEnabled,
        string? triggerKind,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            return Result<PagedResult<HostJobScheduleResponse>>.Failure(
                new Error(
                    JobsErrorCodes.ScheduleValidationFailed,
                    "The job schedule page is invalid.",
                    ErrorType.Validation));
        }

        var parameters = new
        {
            JobDefinitionId = jobDefinitionId,
            Search = NormalizeSearch(search),
            IsEnabled = isEnabled,
            TriggerKind = NormalizeOptional(triggerKind),
            Offset = (page - 1) * pageSize,
            PageSize = pageSize,
        };
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => JobSql.ListSchedulesSqlServer,
            DatabaseProvider.MySql => JobSql.ListSchedulesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var records = await queryExecutor.QueryAsync<JobScheduleDetailRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountSchedules,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<HostJobScheduleResponse>>.Success(
            new PagedResult<HostJobScheduleResponse>(
                records.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    public async Task<Result<IReadOnlyList<HostJobScheduleDefinitionOptionResponse>>>
        ListDefinitionOptionsAsync(
            CancellationToken cancellationToken = default)
    {
        var records = await queryExecutor
            .QueryAsync<JobDefinitionOptionRecord>(
                JobSql.ListEnabledScheduleDefinitionOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostJobScheduleDefinitionOptionResponse>>.Success(
            records.Select(record => new HostJobScheduleDefinitionOptionResponse(
                record.Id,
                record.JobKey,
                record.DisplayName))
                .ToArray());
    }

    public Task<Result<HostJobScheduleCronPreviewResponse>> PreviewCronAsync(
        string cronExpression,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        try
        {
            var normalizedTimeZone = JobScheduleCalculator.NormalizeTimeZoneId(timeZoneId);
            var next = JobScheduleCalculator.GetNextCronOccurrence(
                cronExpression.Trim(),
                normalizedTimeZone,
                clock.UtcNow.ToUniversalTime());
            return Task.FromResult(
                Result<HostJobScheduleCronPreviewResponse>.Success(
                    new HostJobScheduleCronPreviewResponse(next)));
        }
        catch (Exception)
        {
            return Task.FromResult(
                Result<HostJobScheduleCronPreviewResponse>.Failure(
                    new Error(
                        JobsErrorCodes.ScheduleValidationFailed,
                        "The cron preview request is invalid.",
                        ErrorType.Validation)));
        }
    }

    public async Task<Result<HostJobScheduleResponse>> GetByIdAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default) =>
        await FindResultAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);

    public Task<Result<HostJobScheduleResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostJobScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<HostJobScheduleResponse>> PauseAsync(
        Guid actorUserId,
        Guid scheduleId,
        ChangeHostJobScheduleStateRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ChangeStateAsync(
                actorUserId,
                scheduleId,
                request.Version,
                enable: false,
                token),
            cancellationToken);

    public Task<Result<HostJobScheduleResponse>> UpdateAsync(
        Guid actorUserId,
        Guid scheduleId,
        UpdateHostJobScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(
                actorUserId,
                scheduleId,
                request,
                token),
            cancellationToken);

    public Task<Result<HostJobScheduleResponse>> ResumeAsync(
        Guid actorUserId,
        Guid scheduleId,
        ChangeHostJobScheduleStateRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ChangeStateAsync(
                actorUserId,
                scheduleId,
                request.Version,
                enable: true,
                token),
            cancellationToken);

    private async Task<Result<HostJobScheduleResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostJobScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var definition =
            await queryExecutor.QuerySingleOrDefaultAsync<JobDefinitionRecord>(
                    JobSql.FindDefinitionById,
                    new { Id = request.JobDefinitionId },
                    cancellationToken)
                .ConfigureAwait(false);
        if (definition is null)
        {
            return HostJobDefinitionQueryService
                .DefinitionNotFound<HostJobScheduleResponse>();
        }

        if (!definition.IsEnabled)
        {
            return Failure(
                JobsErrorCodes.DefinitionDisabled,
                ErrorType.Validation,
                "The job definition is disabled.");
        }

        var normalized = Normalize(request);
        if (normalized is null)
        {
            return ValidationFailure();
        }

        var now = clock.UtcNow.ToUniversalTime();
        var nextExecutionAtUtc = normalized.Value.TriggerKind switch
        {
            JobTriggerKinds.OneTime => normalized.Value.OneTimeAtUtc,
            JobTriggerKinds.Cron => JobScheduleCalculator
                .GetNextCronOccurrence(
                    normalized.Value.CronExpression!,
                    normalized.Value.TimeZoneId,
                    now),
            _ => null,
        };
        if (nextExecutionAtUtc is null
            || nextExecutionAtUtc <= now)
        {
            return ValidationFailure();
        }

        var scheduleId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                JobSql.InsertSchedule,
                new
                {
                    Id = scheduleId,
                    request.JobDefinitionId,
                    normalized.Value.TriggerKind,
                    normalized.Value.CronExpression,
                    normalized.Value.TimeZoneId,
                    normalized.Value.OneTimeAtUtc,
                    normalized.Value.MisfirePolicy,
                    IsEnabled = true,
                    NextExecutionAtUtc = nextExecutionAtUtc,
                    CreatedAtUtc = now,
                    CreatedByUserId = actorUserId,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await FindResultAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostJobScheduleResponse>> ChangeStateAsync(
        Guid actorUserId,
        Guid scheduleId,
        int version,
        bool enable,
        CancellationToken cancellationToken)
    {
        var schedule = await queryExecutor
            .QuerySingleOrDefaultAsync<JobScheduleRecord>(
                JobSql.FindScheduleById,
                new { Id = scheduleId },
                cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow.ToUniversalTime();
        DateTimeOffset? nextExecutionAtUtc = schedule.NextExecutionAtUtc;
        if (enable)
        {
            if (schedule.CompletedAtUtc is not null)
            {
                return ValidationFailure();
            }

            nextExecutionAtUtc = string.Equals(
                    schedule.TriggerKind,
                    JobTriggerKinds.Cron,
                    StringComparison.Ordinal)
                ? JobScheduleCalculator.GetNextCronOccurrence(
                    schedule.CronExpression!,
                    schedule.TimeZoneId,
                    now)
                : schedule.OneTimeAtUtc;
        }

        var affected = await commandExecutor.ExecuteAsync(
                enable ? JobSql.ResumeSchedule : JobSql.PauseSchedule,
                new
                {
                    Id = scheduleId,
                    NextExecutionAtUtc = nextExecutionAtUtc,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = version + 1,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await FindResultAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostJobScheduleResponse>> UpdateCoreAsync(
        Guid actorUserId,
        Guid scheduleId,
        UpdateHostJobScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await queryExecutor
            .QuerySingleOrDefaultAsync<JobScheduleRecord>(
                JobSql.FindScheduleById,
                new { Id = scheduleId },
                cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            return NotFound();
        }

        if (schedule.CompletedAtUtc is not null)
        {
            return ValidationFailure();
        }

        var normalized = Normalize(new CreateHostJobScheduleRequest(
            schedule.JobDefinitionId,
            request.TriggerKind,
            request.CronExpression,
            request.TimeZoneId,
            request.OneTimeAtUtc,
            request.MisfirePolicy));
        if (normalized is null)
        {
            return ValidationFailure();
        }

        var now = clock.UtcNow.ToUniversalTime();
        var nextExecutionAtUtc = normalized.Value.TriggerKind switch
        {
            JobTriggerKinds.OneTime => normalized.Value.OneTimeAtUtc,
            JobTriggerKinds.Cron => JobScheduleCalculator
                .GetNextCronOccurrence(
                    normalized.Value.CronExpression!,
                    normalized.Value.TimeZoneId,
                    now),
            _ => null,
        };
        if (nextExecutionAtUtc is null
            || nextExecutionAtUtc <= now)
        {
            return ValidationFailure();
        }

        var affected = await commandExecutor.ExecuteAsync(
                JobSql.UpdateSchedule,
                new
                {
                    Id = scheduleId,
                    normalized.Value.TriggerKind,
                    normalized.Value.CronExpression,
                    normalized.Value.TimeZoneId,
                    normalized.Value.OneTimeAtUtc,
                    normalized.Value.MisfirePolicy,
                    NextExecutionAtUtc = nextExecutionAtUtc,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = request.Version + 1,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await FindResultAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostJobScheduleResponse>> FindResultAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var schedule = await FindRecordAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
        return schedule is null
            ? NotFound()
            : Result<HostJobScheduleResponse>.Success(Map(schedule));
    }

    private Task<JobScheduleDetailRecord?> FindRecordAsync(
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<JobScheduleDetailRecord>(
            JobSql.FindScheduleDetailById,
            new { Id = scheduleId },
            cancellationToken);

    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : $"%{trimmed}%";
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static HostJobScheduleResponse Map(JobScheduleDetailRecord record) =>
        new(
            record.Id,
            record.JobDefinitionId,
            record.JobDefinitionJobKey,
            record.JobDefinitionDisplayName,
            record.TriggerKind,
            record.CronExpression,
            record.TimeZoneId,
            record.OneTimeAtUtc,
            record.MisfirePolicy,
            record.IsEnabled,
            record.NextExecutionAtUtc,
            record.LastExecutionAtUtc,
            record.CompletedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static NormalizedSchedule? Normalize(
        CreateHostJobScheduleRequest request)
    {
        try
        {
            var triggerKind = request.TriggerKind?.Trim();
            var misfirePolicy = request.MisfirePolicy?.Trim();
            if (misfirePolicy is not (
                    JobMisfirePolicies.Skip
                    or JobMisfirePolicies.FireOnce))
            {
                return null;
            }

            var timeZoneId = JobScheduleCalculator.NormalizeTimeZoneId(
                request.TimeZoneId);
            if (triggerKind == JobTriggerKinds.OneTime)
            {
                if (request.CronExpression is not null
                    || request.OneTimeAtUtc is null
                    || misfirePolicy != JobMisfirePolicies.FireOnce)
                {
                    return null;
                }

                return new NormalizedSchedule(
                    triggerKind,
                    null,
                    timeZoneId,
                    request.OneTimeAtUtc.Value.ToUniversalTime(),
                    misfirePolicy);
            }

            if (triggerKind != JobTriggerKinds.Cron
                || string.IsNullOrWhiteSpace(request.CronExpression)
                || request.OneTimeAtUtc is not null)
            {
                return null;
            }

            var cronExpression = request.CronExpression.Trim();
            _ = CronExpression.Parse(cronExpression, CronFormat.Standard);
            return new NormalizedSchedule(
                triggerKind,
                cronExpression,
                timeZoneId,
                null,
                misfirePolicy);
        }
        catch (CronFormatException)
        {
            return null;
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static Result<HostJobScheduleResponse> ValidationFailure() =>
        Failure(
            JobsErrorCodes.ScheduleValidationFailed,
            ErrorType.Validation,
            "The job schedule is invalid.");

    private static Result<HostJobScheduleResponse> NotFound() =>
        Failure(
            JobsErrorCodes.ScheduleNotFound,
            ErrorType.NotFound,
            "The job schedule was not found.");

    private static Result<HostJobScheduleResponse> ConcurrencyConflict() =>
        Failure(
            JobsErrorCodes.ScheduleConcurrencyConflict,
            ErrorType.Conflict,
            "The job schedule changed concurrently.");

    private static Result<HostJobScheduleResponse> Failure(
        string code,
        ErrorType type,
        string message) =>
        Result<HostJobScheduleResponse>.Failure(new Error(code, message, type));

    private readonly record struct NormalizedSchedule(
        string TriggerKind,
        string? CronExpression,
        string TimeZoneId,
        DateTimeOffset? OneTimeAtUtc,
        string MisfirePolicy);
}
