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

internal sealed class ProductTagTargetQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductTagTargetResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductTagTargetListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductTagTargetResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductTagTargetSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductTagTargetSql.PageMySqlStatement,
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
                                .ReadAsync<ProductTagTargetRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductTagTargetResponse>>.Success(
                    new PagedResult<ProductTagTargetResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductTagTargetResponse>> GetByIdAsync(
                Guid productTagTargetId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductTagTargetRecord>(
                        ProductTagTargetSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productTagTargetId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductTagTargetResponse>.Success(Map(record));
            }

        internal Task<Result<ProductTagTargetResponse>> FindByIdAsync(
            Guid productTagTargetId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productTagTargetId, cancellationToken);

    private static ProductTagTargetResponse Map(
        ProductTagTargetRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.Name,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<ProductTagTargetResponse> NotFound() =>
        ProductTagTargetFeatureErrors.NotFound();

}

internal sealed class ProductTagTargetManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductTagTargetQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductTagTargetResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductTagTargetResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductTagTargetResponse>.Failure(new Error(
                "catalog.tags.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductTagTargetResponse>> CreateAsync(
        CreateProductTagTargetRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductTagTargetResponse>> CreateCoreAsync(
        CreateProductTagTargetRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var productTagTargetId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagTargetSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productTagTargetId, ["Name"] = request.Name, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productTagTargetId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductTagTargetResponse>> UpdateAsync(
        Guid productTagTargetId,
        UpdateProductTagTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productTagTargetId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagTargetResponse>> UpdateCoreAsync(
        Guid productTagTargetId,
        UpdateProductTagTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.Name);
    if (validationError is not null)
    {
        return validationError;
    }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagTargetSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productTagTargetId, ["Name"] = request.Name, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagTargetId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productTagTargetId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductTagTargetResponse>> DeleteAsync(
        Guid productTagTargetId,
            DeleteProductTagTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productTagTargetId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagTargetResponse>> DeleteCoreAsync(
        Guid productTagTargetId,
            DeleteProductTagTargetRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productTagTargetId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagTargetSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productTagTargetId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagTargetId,
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

    private static Result<ProductTagTargetResponse>? ValidateWriteRequest(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }
        return null;
    }

    private static Result<ProductTagTargetResponse> ValidationFailure(
        string field) =>
        Result<ProductTagTargetResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.tags.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductTagTargetResponse>> ResolveWriteFailureAsync(
        Guid productTagTargetId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductTagTargetRecord>(
                ProductTagTargetSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productTagTargetId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductTagTargetResponse> VersionConflict() =>
        Result<ProductTagTargetResponse>.Failure(new Error(
            ProductTagTargetErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<ProductTagTargetResponse> NotFound() =>
        ProductTagTargetFeatureErrors.NotFound();
}

internal static class ProductTagTargetFeatureErrors
{
    internal static Result<ProductTagTargetResponse> NotFound() =>
        Result<ProductTagTargetResponse>.Failure(new Error(
            ProductTagTargetErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
