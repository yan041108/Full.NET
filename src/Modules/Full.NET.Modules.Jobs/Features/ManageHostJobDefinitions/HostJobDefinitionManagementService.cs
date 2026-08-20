using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;

/// <summary>
/// Host 任务定义创建、更新、禁用与硬删除管理服务。删除前置校验：必须已禁用、无启用计划、无未终结执行记录，
/// 满足条件后先级联清理关联计划再删除定义本身；
/// 创建/更新时校验 JobKey 格式与 HandlerKind/Args 契约，不再要求 JobKey 对应编译期 Handler。
/// </summary>
internal sealed class HostJobDefinitionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostJobDefinitionQueryService queries,
    JobHandlerKindRegistry handlerKindRegistry,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex JobKeyPattern = new(
        @"^[a-z][a-z0-9._-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<HostJobDefinitionResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostJobDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<HostJobDefinitionResponse>> UpdateAsync(
        Guid actorUserId,
        Guid definitionId,
        UpdateHostJobDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(actorUserId, definitionId, request, token),
            cancellationToken);

    public Task<Result<HostJobDefinitionResponse>> DisableAsync(
        Guid actorUserId,
        Guid definitionId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(actorUserId, definitionId, version, token),
            cancellationToken);

    public Task<Result<bool>> DeleteAsync(
        Guid definitionId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DeleteCoreAsync(definitionId, version, token),
            cancellationToken);

    private async Task<Result<HostJobDefinitionResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostJobDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var handlerKind = request.HandlerKind?.Trim() ?? string.Empty;
        if (handlerKind.Length == 0)
        {
            return HandlerKindRequiredFailure();
        }

        var validation = ValidateDefinition(
            request.JobKey,
            handlerKind,
            request.Args,
            request.DisplayName,
            request.Description,
            request.GroupName,
            rejectSensitivePlainHeaders: true);
        if (validation is not null)
        {
            return validation;
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<JobDefinitionRecord>(
                JobSql.FindDefinitionByJobKey,
                new { JobKey = request.JobKey.Trim() },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<HostJobDefinitionResponse>.Failure(new Error(
                JobsErrorCodes.DefinitionJobKeyExists,
                "The job key already exists.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var definitionId = idGenerator.NewId();
        var argsJson = HostJobDefinitionArgsMapper.SerializeForStorage(
            handlerKind,
            request.Args);
        await commandExecutor.ExecuteAsync(
                JobSql.InsertDefinition,
                new
                {
                    Id = definitionId,
                    JobKey = request.JobKey.Trim(),
                    HandlerKind = handlerKind,
                    ArgsJson = argsJson,
                    DisplayName = request.DisplayName.Trim(),
                    Description = NormalizeDescription(request.Description),
                    GroupName = NormalizeGroupName(request.GroupName),
                    IsEnabled = true,
                    AllowConcurrentExecutions = request.AllowConcurrentExecutions,
                    CreatedAtUtc = now,
                    CreatedByUserId = actorUserId,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(definitionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostJobDefinitionResponse>> UpdateCoreAsync(
        Guid actorUserId,
        Guid definitionId,
        UpdateHostJobDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var handlerKind = request.HandlerKind?.Trim() ?? string.Empty;
        if (handlerKind.Length == 0)
        {
            return HandlerKindRequiredFailure();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Trim().Length > 200)
        {
            return ValidationFailure();
        }

        var validation = ValidateHandlerKindAndArgs(
            handlerKind,
            request.Args,
            rejectSensitivePlainHeaders: true);
        if (validation is not null)
        {
            return validation;
        }

        var groupName = NormalizeGroupName(request.GroupName);
        if (groupName is { Length: > 64 })
        {
            return ValidationFailure();
        }

        var now = clock.UtcNow;
        var argsJson = HostJobDefinitionArgsMapper.SerializeForStorage(
            handlerKind,
            request.Args);
        var affected = await commandExecutor.ExecuteAsync(
                JobSql.UpdateDefinition,
                new
                {
                    Id = definitionId,
                    DisplayName = request.DisplayName.Trim(),
                    Description = NormalizeDescription(request.Description),
                    GroupName = groupName,
                    HandlerKind = handlerKind,
                    ArgsJson = argsJson,
                    AllowConcurrentExecutions = request.AllowConcurrentExecutions,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = request.Version + 1,
                    Version = request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await queries.GetByIdAsync(definitionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid definitionId,
        int version,
        CancellationToken cancellationToken)
    {
        var activeSchedules = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountActiveSchedulesByDefinition,
                new { JobDefinitionId = definitionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (activeSchedules > 0)
        {
            return Result<bool>.Failure(new Error(
                JobsErrorCodes.DefinitionHasActiveDependents,
                "The job definition still has active schedules.",
                ErrorType.BusinessRule));
        }

        var activeExecutions = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountActiveExecutionsByDefinition,
                new { JobDefinitionId = definitionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (activeExecutions > 0)
        {
            return Result<bool>.Failure(new Error(
                JobsErrorCodes.DefinitionHasActiveDependents,
                "The job definition still has active executions.",
                ErrorType.BusinessRule));
        }

        await commandExecutor.ExecuteAsync(
                JobSql.DeleteSchedulesByDefinition,
                new { JobDefinitionId = definitionId },
                cancellationToken)
            .ConfigureAwait(false);

        var affected = await commandExecutor.ExecuteAsync(
                JobSql.DeleteDefinition,
                new { Id = definitionId, Version = version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            var existing = await queries.GetByIdAsync(definitionId, cancellationToken)
                .ConfigureAwait(false);
            return existing.IsSuccess
                ? Result<bool>.Failure(new Error(
                    JobsErrorCodes.DefinitionConcurrencyConflict,
                    "The job definition changed concurrently.",
                    ErrorType.Conflict))
                : HostJobDefinitionQueryService.DefinitionNotFound<bool>();
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<HostJobDefinitionResponse>> DisableCoreAsync(
        Guid actorUserId,
        Guid definitionId,
        int version,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                JobSql.DisableDefinition,
                new
                {
                    Id = definitionId,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = version + 1,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            var existing = await queries.GetByIdAsync(definitionId, cancellationToken)
                .ConfigureAwait(false);
            return existing.IsSuccess
                ? ConcurrencyConflict()
                : HostJobDefinitionQueryService.DefinitionNotFound<HostJobDefinitionResponse>();
        }

        return await queries.GetByIdAsync(definitionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private Result<HostJobDefinitionResponse>? ValidateDefinition(
        string jobKey,
        string handlerKind,
        HttpJobArgs? args,
        string displayName,
        string? description,
        string? groupName,
        bool rejectSensitivePlainHeaders)
    {
        var normalizedKey = jobKey?.Trim() ?? string.Empty;
        var normalizedName = displayName?.Trim() ?? string.Empty;
        if (!JobKeyPattern.IsMatch(normalizedKey))
        {
            return ValidationFailure();
        }

        var handlerValidation = ValidateHandlerKindAndArgs(
            handlerKind,
            args,
            rejectSensitivePlainHeaders);
        if (handlerValidation is not null)
        {
            return handlerValidation;
        }

        if (normalizedName.Length is < 1 or > 200)
        {
            return ValidationFailure();
        }

        if (description is not null && description.Trim().Length > 500)
        {
            return ValidationFailure();
        }

        if (NormalizeGroupName(groupName) is { Length: > 64 })
        {
            return ValidationFailure();
        }

        return null;
    }

    private Result<HostJobDefinitionResponse>? ValidateHandlerKindAndArgs(
        string handlerKind,
        HttpJobArgs? args,
        bool rejectSensitivePlainHeaders)
    {
        if (!JobHandlerKinds.All.Contains(handlerKind, StringComparer.Ordinal)
            || !handlerKindRegistry.TryGetExecutor(handlerKind, out _))
        {
            return ValidationFailure();
        }

        if (string.Equals(handlerKind, JobHandlerKinds.Ping, StringComparison.Ordinal))
        {
            return args is not null ? ValidationFailure() : null;
        }

        if (!string.Equals(handlerKind, JobHandlerKinds.Http, StringComparison.Ordinal)
            || args is null)
        {
            return ValidationFailure();
        }

        if (!HttpJobArgsValidator.TryValidate(
                args,
                rejectSensitivePlainHeaders,
                out _))
        {
            return rejectSensitivePlainHeaders
                && args.Headers is not null
                && args.Headers.Keys.Any(HttpJobArgsValidator.IsSensitiveHeaderName)
                ? SensitiveHeaderFailure()
                : ValidationFailure();
        }

        return null;
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? NormalizeGroupName(string? groupName)
    {
        var normalized = groupName?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<HostJobDefinitionResponse> ValidationFailure() =>
        Result<HostJobDefinitionResponse>.Failure(new Error(
            JobsErrorCodes.DefinitionValidationFailed,
            "The job definition is invalid.",
            ErrorType.Validation));

    private static Result<HostJobDefinitionResponse> HandlerKindRequiredFailure() =>
        Result<HostJobDefinitionResponse>.Failure(new Error(
            JobsErrorCodes.HandlerKindRequired,
            "The handler kind is required.",
            ErrorType.Validation));

    private static Result<HostJobDefinitionResponse> SensitiveHeaderFailure() =>
        Result<HostJobDefinitionResponse>.Failure(new Error(
            JobsErrorCodes.SensitiveHeaderInPlainHeaders,
            "Sensitive headers must use secretHeaders.",
            ErrorType.Validation));

    private static Result<HostJobDefinitionResponse> ConcurrencyConflict() =>
        Result<HostJobDefinitionResponse>.Failure(new Error(
            JobsErrorCodes.DefinitionConcurrencyConflict,
            "The job definition changed concurrently.",
            ErrorType.Conflict));
}
