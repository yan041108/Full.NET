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

/// <summary>Host 任务定义创建、更新与禁用。</summary>
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

    private async Task<Result<HostJobDefinitionResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostJobDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDefinition(request.JobKey, request.DisplayName, request.Description);
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

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                JobSql.UpdateDefinition,
                new
                {
                    Id = definitionId,
                    DisplayName = request.DisplayName.Trim(),
                    Description = NormalizeDescription(request.Description),
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
        string? description)
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

        return null;
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
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
