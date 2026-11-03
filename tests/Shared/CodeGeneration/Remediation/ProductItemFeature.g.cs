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

internal sealed class ProductItemQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductItemResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductItemListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductItemResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductItemSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductItemSql.PageMySqlStatement,
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
                                .ReadAsync<ProductItemRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductItemResponse>>.Success(
                    new PagedResult<ProductItemResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductItemResponse>> GetByIdAsync(
                Guid productItemId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductItemRecord>(
                        ProductItemSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productItemId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductItemResponse>.Success(Map(record));
            }

        internal Task<Result<ProductItemResponse>> FindByIdAsync(
            Guid productItemId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productItemId, cancellationToken);

    private static ProductItemResponse Map(
        ProductItemRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.ProductId,
            record.Description,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<ProductItemResponse> NotFound() =>
        ProductItemFeatureErrors.NotFound();

}

internal sealed class ProductItemManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductItemQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductItemResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductItemResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductItemResponse>.Failure(new Error(
                "catalog.product_items.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductItemResponse>> CreateAsync(
        CreateProductItemRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductItemResponse>> CreateCoreAsync(
        CreateProductItemRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.ProductId,
            request.Description);
        if (validationError is not null)
        {
            return validationError;
        }

        var productItemId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductItemSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productItemId, ["ProductId"] = request.ProductId, ["Description"] = request.Description, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productItemId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductItemResponse>> UpdateAsync(
        Guid productItemId,
        UpdateProductItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productItemId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductItemResponse>> UpdateCoreAsync(
        Guid productItemId,
        UpdateProductItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.ProductId,
        request.Description);
    if (validationError is not null)
    {
        return validationError;
    }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductItemSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productItemId, ["ProductId"] = request.ProductId, ["Description"] = request.Description, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productItemId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productItemId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductItemResponse>> DeleteAsync(
        Guid productItemId,
            DeleteProductItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productItemId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductItemResponse>> DeleteCoreAsync(
        Guid productItemId,
            DeleteProductItemRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productItemId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                ProductItemSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productItemId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productItemId,
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

    private static Result<ProductItemResponse>? ValidateWriteRequest(
        Guid productId,
        string description)
    {
        if (productId == Guid.Empty)
        {
            return ValidationFailure("ProductId");
        }

        if (string.IsNullOrWhiteSpace(description) || description is { Length: > 64 })
        {
            return ValidationFailure("Description");
        }
        return null;
    }

    private static Result<ProductItemResponse> ValidationFailure(
        string field) =>
        Result<ProductItemResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.product_items.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductItemResponse>> ResolveWriteFailureAsync(
        Guid productItemId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductItemRecord>(
                ProductItemSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productItemId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductItemResponse> VersionConflict() =>
        Result<ProductItemResponse>.Failure(new Error(
            ProductItemErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<ProductItemResponse> NotFound() =>
        ProductItemFeatureErrors.NotFound();
}

internal static class ProductItemFeatureErrors
{
    internal static Result<ProductItemResponse> NotFound() =>
        Result<ProductItemResponse>.Failure(new Error(
            ProductItemErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
