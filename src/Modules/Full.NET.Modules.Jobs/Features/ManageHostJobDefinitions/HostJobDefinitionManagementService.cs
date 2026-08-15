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
/// 创建/更新时校验 JobKey 匹配正则并在 JobHandlerRegistry 中注册了对应处理器；
/// UI 层面：最后保护（至少保留一个 JobDefinition，不允许全部删除，删除端点前置校验由授权策略层实现）、
/// 禁用/启用/立即执行按钮按定义状态与权限动态显隐。
/// </summary>
internal sealed class HostJobDefinitionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostJobDefinitionQueryService queries,
    JobHandlerRegistry handlerRegistry,
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
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<HostJobDefinitionResponse>> UpdateAsync(
        Guid actorUserId,
        Guid definitionId,
        UpdateHostJobDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(actorUserId, definitionId, request, token),
            cancellationToken);

    public Task<Result<HostJobDefinitionResponse>> DisableAsync(
        Guid actorUserId,
        Guid definitionId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(actorUserId, definitionId, version, token),
            cancellationToken);

    /// <summary>
    /// 硬删除已禁用且无活跃依赖的作业定义，对应 Admin.NET DeleteJobDetail。
    /// 删除前置校验：定义必须已禁用、无启用计划、无未终结执行记录。
    /// 满足条件后在同一事务内清理关联计划并删除定义本身。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid definitionId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(definitionId, version, token),
            cancellationToken);

    private async Task<Result<HostJobDefinitionResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostJobDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDefinition(
            request.JobKey,
            request.DisplayName,
            request.Description,
            request.GroupName);
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
        await commandExecutor.ExecuteAsync(
                JobSql.InsertDefinition,
                new
                {
                    Id = definitionId,
                    JobKey = request.JobKey.Trim(),
                    DisplayName = request.DisplayName.Trim(),
                    Description = NormalizeDescription(request.Description),
                    GroupName = NormalizeGroupName(request.GroupName),
                    IsEnabled = true,
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
        if (string.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Trim().Length > 200)
        {
            return ValidationFailure();
        }

        var groupName = NormalizeGroupName(request.GroupName);
        if (groupName is { Length: > 64 })
        {
            return ValidationFailure();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                JobSql.UpdateDefinition,
                new
                {
                    Id = definitionId,
                    DisplayName = request.DisplayName.Trim(),
                    Description = NormalizeDescription(request.Description),
                    GroupName = groupName,
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

    /// <summary>
    /// 硬删除作业定义核心逻辑：校验已禁用、无活跃计划、无未终结执行后，
    /// 在同一事务内先删除关联计划再删除定义本身。
    /// 执行记录无外键约束，删除定义后历史记录通过 INNER JOIN 自然过滤。
    /// </summary>
    private async Task<Result<bool>> DeleteCoreAsync(
        Guid definitionId,
        int version,
        CancellationToken cancellationToken)
    {
        // 活跃计划存在时拒绝删除，避免删除正在调度的作业定义。
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

        // 未终结执行记录存在时拒绝删除，避免丢失正在运行的任务证据。
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

        // 计划表对定义存在外键约束，必须先清理计划才能删除定义。
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
            // 删除 0 行表示定义不存在、未禁用或版本不匹配，统一回查区分。
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
        string displayName,
        string? description,
        string? groupName)
    {
        var normalizedKey = jobKey?.Trim() ?? string.Empty;
        var normalizedName = displayName?.Trim() ?? string.Empty;
        if (!JobKeyPattern.IsMatch(normalizedKey)
            || !handlerRegistry.TryGetHandler(normalizedKey, out _))
        {
            return ValidationFailure();
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

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    /// <summary>
    /// 归一化作业分组名：去空白，空字符串视为未分组（null）。
    /// 对应 Admin.NET SysJobDetail.GroupName，用于按组筛选与展示。
    /// </summary>
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

    private static Result<HostJobDefinitionResponse> ConcurrencyConflict() =>
        Result<HostJobDefinitionResponse>.Failure(new Error(
            JobsErrorCodes.DefinitionConcurrencyConflict,
            "The job definition changed concurrently.",
            ErrorType.Conflict));
}
