#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Acme.Remediation.Generated;

internal sealed class WorkbenchQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<WorkbenchResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                WorkbenchListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<WorkbenchResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        WorkbenchSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        WorkbenchSql.PageMySqlStatement,
                    _ => throw new InvalidOperationException(
                        "The configured database provider is not supported."),
                };
                var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                        statement,
                        query.Parameters(offset, pageSize),
                        async (reader, _) =>
                        {
                            var total = await reader.ReadSingleOrDefaultAsync<long>()
                                .ConfigureAwait(false);
                            var rows = await reader
                                .ReadAsync<WorkbenchRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<WorkbenchResponse>>.Success(
                    new PagedResult<WorkbenchResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<WorkbenchResponse>> GetByIdAsync(
                Guid workbenchId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<WorkbenchRecord>(
                        WorkbenchSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = workbenchId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<WorkbenchResponse>.Success(Map(record));
            }

        internal Task<Result<WorkbenchResponse>> FindByIdAsync(
            Guid workbenchId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(workbenchId, cancellationToken);

    private static WorkbenchResponse Map(
        WorkbenchRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.Name,
            record.Quantity,
            record.Price,
            record.IsReady,
            record.ReferenceId,
            record.OccurredAtUtc,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<WorkbenchResponse> NotFound() =>
        WorkbenchFeatureErrors.NotFound();

}

internal sealed class WorkbenchManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    WorkbenchQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<WorkbenchResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<WorkbenchResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<WorkbenchResponse>.Failure(new Error(
                "catalog.workbenches.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<WorkbenchResponse>> CreateAsync(
        CreateWorkbenchRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<WorkbenchResponse>> CreateCoreAsync(
        CreateWorkbenchRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name,
            request.Price,
            request.OccurredAtUtc);
        if (validationError is not null)
        {
            return validationError;
        }

        var workbenchId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                WorkbenchSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = workbenchId, ["Name"] = request.Name, ["Quantity"] = request.Quantity, ["Price"] = request.Price, ["IsReady"] = request.IsReady, ["ReferenceId"] = request.ReferenceId, ["OccurredAtUtc"] = request.OccurredAtUtc?.ToUniversalTime(), ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                workbenchId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<WorkbenchResponse>> UpdateAsync(
        Guid workbenchId,
        UpdateWorkbenchRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                workbenchId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<WorkbenchResponse>> UpdateCoreAsync(
        Guid workbenchId,
        UpdateWorkbenchRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.Name,
        request.Price,
        request.OccurredAtUtc);
    if (validationError is not null)
    {
        return validationError;
    }

        var affectedRows = await commandExecutor.ExecuteAsync(
                WorkbenchSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = workbenchId, ["Name"] = request.Name, ["Quantity"] = request.Quantity, ["Price"] = request.Price, ["IsReady"] = request.IsReady, ["ReferenceId"] = request.ReferenceId, ["OccurredAtUtc"] = request.OccurredAtUtc?.ToUniversalTime(), ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    workbenchId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                workbenchId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<WorkbenchResponse>> DeleteAsync(
        Guid workbenchId,
            DeleteWorkbenchRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                workbenchId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<WorkbenchResponse>> DeleteCoreAsync(
        Guid workbenchId,
            DeleteWorkbenchRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                workbenchId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                WorkbenchSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = workbenchId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    workbenchId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return existing;
    }
    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable
            || currentTenant.IsHost
            || currentTenant.Id is null)
        {
            throw new TenantContextMissingException(
                "catalog.tenant_context_required");
        }
    }

    private static Result<WorkbenchResponse>? ValidateWriteRequest(
        string name,
        decimal price,
        DateTimeOffset? occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }

        if ((price is decimal priceValue && (decimal.Round(priceValue, 4) != priceValue || priceValue <= -100000000000000m || priceValue >= 100000000000000m)))
        {
            return ValidationFailure("Price");
        }

        if ((occurredAtUtc is DateTimeOffset occurredAtUtcValue && (occurredAtUtcValue.ToUniversalTime().Year < 1000 || occurredAtUtcValue.UtcTicks % 10 != 0)))
        {
            return ValidationFailure("OccurredAtUtc");
        }
        return null;
    }

    private static Result<WorkbenchResponse> ValidationFailure(
        string field) =>
        Result<WorkbenchResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.workbenches.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<WorkbenchResponse>> ResolveWriteFailureAsync(
        Guid workbenchId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<WorkbenchRecord>(
                WorkbenchSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = workbenchId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<WorkbenchResponse> VersionConflict() =>
        Result<WorkbenchResponse>.Failure(new Error(
            WorkbenchErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<WorkbenchResponse> NotFound() =>
        WorkbenchFeatureErrors.NotFound();
}

internal static class WorkbenchFeatureErrors
{
    internal static Result<WorkbenchResponse> NotFound() =>
        Result<WorkbenchResponse>.Failure(new Error(
            WorkbenchErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
