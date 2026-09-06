using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Persistence;

namespace Full.NET.Modules.Calendar.Features.ManageMyPersonalSchedules;

/// <summary>当前用户个人日程创建、更新、删除与状态切换；只影响受信作用域内所属用户的行。</summary>
internal sealed class PersonalScheduleManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>个人日程内容允许的最大字符数。</summary>
    internal const int MaxContentLength = 256;

    /// <summary>
    /// 为当前用户创建一条个人日程，初始状态固定为 pending。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>新建日程或稳定业务错误。</returns>
    public Task<Result<PersonalScheduleResponse>> CreateAsync(
        Guid ownerUserId,
        CreatePersonalScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(ownerUserId, request, token),
            cancellationToken);

    /// <summary>
    /// 更新当前用户的一条个人日程，使用乐观版本号防止并发覆盖。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="scheduleId">个人日程标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>更新后的日程或稳定业务错误。</returns>
    public Task<Result<PersonalScheduleResponse>> UpdateAsync(
        Guid ownerUserId,
        Guid scheduleId,
        UpdatePersonalScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(ownerUserId, scheduleId, request, token),
            cancellationToken);

    /// <summary>
    /// 删除当前用户的一条个人日程，要求版本号匹配。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="scheduleId">个人日程标识。</param>
    /// <param name="version">客户端感知的乐观并发版本号。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid ownerUserId,
        Guid scheduleId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(ownerUserId, scheduleId, version, token),
            cancellationToken);

    /// <summary>
    /// 切换当前用户个人日程的完成状态，completed 时写入 CompletedAtUtc。
    /// </summary>
    /// <param name="ownerUserId">所属用户标识，必须来自受信 sub 声明。</param>
    /// <param name="scheduleId">个人日程标识。</param>
    /// <param name="request">状态切换请求。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>更新后的日程或稳定业务错误。</returns>
    public Task<Result<PersonalScheduleResponse>> SetStatusAsync(
        Guid ownerUserId,
        Guid scheduleId,
        SetPersonalScheduleStatusRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => SetStatusCoreAsync(ownerUserId, scheduleId, request, token),
            cancellationToken);

    private async Task<Result<PersonalScheduleResponse>> CreateCoreAsync(
        Guid ownerUserId,
        CreatePersonalScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var contentResult = ValidateContent(request.Content);
        if (!contentResult.IsSuccess)
        {
            return Result<PersonalScheduleResponse>.Failure(contentResult.Error!);
        }

        var startAtUtc = request.StartAtUtc.ToUniversalTime();
        var endAtUtc = request.EndAtUtc.ToUniversalTime();
        if (!IsValidTimeRange(startAtUtc, endAtUtc))
        {
            return InvalidTimeRange<PersonalScheduleResponse>();
        }

        var scope = CalendarScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var scheduleId = idGenerator.NewId();
        var insertStatement = scope.TenantId is null
            ? PersonalScheduleSql.InsertHost
            : PersonalScheduleSql.InsertTenant;
        var parameters = CalendarSqlParameters.Create(
            ("Id", scheduleId),
            ("OwnerUserId", ownerUserId),
            ("Content", contentResult.Value!),
            ("StartAtUtc", startAtUtc),
            ("EndAtUtc", endAtUtc),
            ("Status", PersonalScheduleStatuses.Pending),
            ("CreatedAtUtc", now),
            ("Version", 1));
        if (scope.TenantId is { } tenantId)
        {
            parameters["TenantId"] = tenantId;
        }

        await commandExecutor.ExecuteAsync(
                insertStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        var created = await queryExecutor.QuerySingleOrDefaultAsync<PersonalScheduleRecord>(
                PersonalScheduleSql.FindForOwnerById,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PersonalScheduleResponse>.Success(Map(created!));
    }

    private async Task<Result<PersonalScheduleResponse>> UpdateCoreAsync(
        Guid ownerUserId,
        Guid scheduleId,
        UpdatePersonalScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var contentResult = ValidateContent(request.Content);
        if (!contentResult.IsSuccess)
        {
            return Result<PersonalScheduleResponse>.Failure(contentResult.Error!);
        }

        var startAtUtc = request.StartAtUtc.ToUniversalTime();
        var endAtUtc = request.EndAtUtc.ToUniversalTime();
        if (!IsValidTimeRange(startAtUtc, endAtUtc))
        {
            return InvalidTimeRange<PersonalScheduleResponse>();
        }

        var scope = CalendarScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                PersonalScheduleSql.UpdateForOwner,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId),
                    ("Content", contentResult.Value!),
                    ("StartAtUtc", startAtUtc),
                    ("EndAtUtc", endAtUtc),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version),
                    ("NextVersion", request.Version + 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return await ResolveWriteConflictAsync<PersonalScheduleResponse>(
                    ownerUserId,
                    scheduleId,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await FindResultAsync(ownerUserId, scheduleId, scope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid ownerUserId,
        Guid scheduleId,
        int version,
        CancellationToken cancellationToken)
    {
        var scope = CalendarScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                PersonalScheduleSql.DeleteForOwner,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return await ResolveWriteConflictAsync<bool>(
                    ownerUserId,
                    scheduleId,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<PersonalScheduleResponse>> SetStatusCoreAsync(
        Guid ownerUserId,
        Guid scheduleId,
        SetPersonalScheduleStatusRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(request.Status);
        if (normalizedStatus is null)
        {
            return InvalidStatus<PersonalScheduleResponse>();
        }

        var scope = CalendarScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var completedAtUtc = normalizedStatus == PersonalScheduleStatuses.Completed
            ? now
            : (DateTimeOffset?)null;
        var affected = await commandExecutor.ExecuteAsync(
                PersonalScheduleSql.SetStatusForOwner,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId),
                    ("Status", normalizedStatus),
                    ("CompletedAtUtc", completedAtUtc),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version),
                    ("NextVersion", request.Version + 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return await ResolveWriteConflictAsync<PersonalScheduleResponse>(
                    ownerUserId,
                    scheduleId,
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await FindResultAsync(ownerUserId, scheduleId, scope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<T>> ResolveWriteConflictAsync<T>(
        Guid ownerUserId,
        Guid scheduleId,
        CalendarScope scope,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<PersonalScheduleRecord>(
                PersonalScheduleSql.FindForOwnerById,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return existing is null
            ? NotFound<T>()
            : ConcurrencyConflict<T>();
    }

    private async Task<Result<PersonalScheduleResponse>> FindResultAsync(
        Guid ownerUserId,
        Guid scheduleId,
        CalendarScope scope,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<PersonalScheduleRecord>(
                PersonalScheduleSql.FindForOwnerById,
                CalendarSqlParameters.Create(
                    ("Id", scheduleId),
                    ("OwnerUserId", ownerUserId),
                    ("ScopeTenantId", scope.TenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound<PersonalScheduleResponse>()
            : Result<PersonalScheduleResponse>.Success(Map(record));
    }

    internal static PersonalScheduleResponse Map(PersonalScheduleRecord record) =>
        new(
            record.Id,
            record.Content,
            record.StartAtUtc,
            record.EndAtUtc,
            record.Status,
            record.CompletedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    internal static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return NormalizeStatus(status.Trim());
    }

    internal static string? NormalizeStatus(string status) =>
        status is PersonalScheduleStatuses.Pending or PersonalScheduleStatuses.Completed
            ? status
            : null;

    internal static Result<string> ValidateContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result<string>.Failure(new Error(
                CalendarErrorCodes.PersonalScheduleInvalidContent,
                "The personal schedule content is invalid.",
                ErrorType.Validation));
        }

        var trimmed = content.Trim();
        if (trimmed.Length > MaxContentLength)
        {
            return Result<string>.Failure(new Error(
                CalendarErrorCodes.PersonalScheduleInvalidContent,
                "The personal schedule content is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(trimmed);
    }

    internal static bool IsValidTimeRange(
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc) =>
        endAtUtc >= startAtUtc;

    internal static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            CalendarErrorCodes.PersonalScheduleNotFound,
            "The personal schedule was not found.",
            ErrorType.NotFound));

    private static Result<T> InvalidTimeRange<T>() =>
        Result<T>.Failure(new Error(
            CalendarErrorCodes.PersonalScheduleInvalidTimeRange,
            "The personal schedule time range is invalid.",
            ErrorType.Validation));

    private static Result<T> InvalidStatus<T>() =>
        Result<T>.Failure(new Error(
            CalendarErrorCodes.PersonalScheduleInvalidStatus,
            "The personal schedule status is invalid.",
            ErrorType.Validation));

    private static Result<T> ConcurrencyConflict<T>() =>
        Result<T>.Failure(new Error(
            CalendarErrorCodes.PersonalScheduleConcurrencyConflict,
            "The personal schedule changed concurrently.",
            ErrorType.Conflict));
}
