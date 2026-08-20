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

/// <summary>
/// Host 任务计划创建、更新、暂停/恢复、删除与 Cron 预览管理服务。
/// 写入前固定时区（NormalizeTimeZoneId）并计算下一次 UTC 触发时刻；支持 Cron 周期性（Cronos 库解析 Standard 格式）
/// 与 OneTime 一次性两类触发器；Cron 预览端点提供表单输入时的下一次触发时刻即时反馈；
/// 创建/更新时校验：起始时间必须早于结束时间、一次性计划不携带 Cron、Cron 表达式合法；
/// 列表响应聚合 NumberOfErrors 供 UI 显示出错次数红色 Tag。
/// 删除前置校验：无 pending/running 未终结执行记录。
/// </summary>
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
                record.HandlerKind,
                record.DisplayName))
                .ToArray());
    }

    public Task<Result<HostJobScheduleCronPreviewResponse>> PreviewCronAsync(
        string cronExpression,
        string timeZoneId,
        int occurrenceCount = 5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var normalizedTimeZone = JobScheduleCalculator.NormalizeTimeZoneId(timeZoneId);
            var now = clock.UtcNow.ToUniversalTime();
            var occurrences = JobScheduleCalculator.GetNextCronOccurrences(
                cronExpression.Trim(),
                normalizedTimeZone,
                now,
                occurrenceCount);
            return Task.FromResult(
                Result<HostJobScheduleCronPreviewResponse>.Success(
                    new HostJobScheduleCronPreviewResponse(
                        JobScheduleCalculator.DescribeCron(cronExpression),
                        occurrences[0],
                        occurrences)));
        }
        catch (CronFormatException)
        {
            return InvalidCronPreview();
        }
        catch (TimeZoneNotFoundException)
        {
            return InvalidCronPreview();
        }
        catch (InvalidTimeZoneException)
        {
            return InvalidCronPreview();
        }
        catch (InvalidOperationException)
        {
            return InvalidCronPreview();
        }
    }

    private static Task<Result<HostJobScheduleCronPreviewResponse>> InvalidCronPreview() =>
        Task.FromResult(
            Result<HostJobScheduleCronPreviewResponse>.Failure(
                new Error(
                    JobsErrorCodes.ScheduleValidationFailed,
                    "The cron preview request is invalid.",
                    ErrorType.Validation)));

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

    /// <summary>
    /// 硬删除任务计划，对应 Admin.NET DeleteJobTrigger。
    /// 删除前置校验：无未终结执行记录（pending/running），否则拒绝以避免丢失运行证据。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid scheduleId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(scheduleId, version, token),
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
                    normalized.Value.StartTime,
                    normalized.Value.EndTime,
                    normalized.Value.Args,
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
            request.MisfirePolicy,
            request.StartTime,
            request.EndTime,
            request.Args));
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
                    normalized.Value.StartTime,
                    normalized.Value.EndTime,
                    normalized.Value.Args,
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

    /// <summary>
    /// 硬删除任务计划核心逻辑：校验无未终结执行记录后删除计划。
    /// 终态执行记录（succeeded/failed）无外键约束，删除计划后通过 JobScheduleId IS NULL 自然保留。
    /// </summary>
    private async Task<Result<bool>> DeleteCoreAsync(
        Guid scheduleId,
        int version,
        CancellationToken cancellationToken)
    {
        // 未终结执行记录存在时拒绝删除，避免丢失正在运行的任务证据。
        var activeExecutions = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountActiveExecutionsBySchedule,
                new { JobScheduleId = scheduleId },
                cancellationToken)
            .ConfigureAwait(false);
        if (activeExecutions > 0)
        {
            return Result<bool>.Failure(new Error(
                JobsErrorCodes.ScheduleHasActiveExecutions,
                "The job schedule still has active executions.",
                ErrorType.BusinessRule));
        }

        var affected = await commandExecutor.ExecuteAsync(
                JobSql.DeleteSchedule,
                new { Id = scheduleId, Version = version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            // 删除 0 行表示计划不存在或版本不匹配，统一回查区分。
            var existing = await FindRecordAsync(scheduleId, cancellationToken)
                .ConfigureAwait(false);
            return existing is null
                ? Result<bool>.Failure(new Error(
                    JobsErrorCodes.ScheduleNotFound,
                    "The job schedule was not found.",
                    ErrorType.NotFound))
                : Result<bool>.Failure(new Error(
                    JobsErrorCodes.ScheduleConcurrencyConflict,
                    "The job schedule changed concurrently.",
                    ErrorType.Conflict));
        }

        return Result<bool>.Success(true);
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
            record.NumberOfRuns,
            record.NumberOfErrors,
            record.StartTime,
            record.EndTime,
            record.Args,
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

            // 起止时间窗口校验：StartTime 必须早于 EndTime，对应 Admin.NET 触发器时间窗口。
            var startTime = request.StartTime?.ToUniversalTime();
            var endTime = request.EndTime?.ToUniversalTime();
            if (startTime is not null && endTime is not null
                && startTime >= endTime)
            {
                return null;
            }

            // Args 参数长度校验，对应 Admin.NET SysJobTrigger.Args。
            var args = NormalizeArgs(request.Args);
            if (args is { Length: > 500 })
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
                    misfirePolicy,
                    startTime,
                    endTime,
                    args);
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
                misfirePolicy,
                startTime,
                endTime,
                args);
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

    /// <summary>
    /// 归一化任务计划参数：去空白，空字符串视为无参数（null）。
    /// 当前仅支持存储与展示，Handler 消费 Args 作为后续扩展点。
    /// </summary>
    private static string? NormalizeArgs(string? args)
    {
        var normalized = args?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
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
        string MisfirePolicy,
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        string? Args);
}
