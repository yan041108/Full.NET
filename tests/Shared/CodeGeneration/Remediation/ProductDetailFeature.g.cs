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

internal sealed class ProductDetailQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductDetailResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductDetailListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductDetailResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductDetailSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductDetailSql.PageMySqlStatement,
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
                                .ReadAsync<ProductDetailRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductDetailResponse>>.Success(
                    new PagedResult<ProductDetailResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductDetailResponse>> GetByIdAsync(
                Guid productDetailId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductDetailRecord>(
                        ProductDetailSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productDetailId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductDetailResponse>.Success(Map(record));
            }

        internal Task<Result<ProductDetailResponse>> FindByIdAsync(
            Guid productDetailId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productDetailId, cancellationToken);

    private static ProductDetailResponse Map(
        ProductDetailRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.ProductId,
            record.Description,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<ProductDetailResponse> NotFound() =>
        ProductDetailFeatureErrors.NotFound();

}

internal sealed class ProductDetailManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductDetailQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductDetailResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductDetailResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductDetailResponse>.Failure(new Error(
                "catalog.product_items.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductDetailResponse>> CreateAsync(
        CreateProductDetailRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductDetailResponse>> CreateCoreAsync(
        CreateProductDetailRequest request,
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

        var productDetailId = idGenerator.NewId();

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductDetailSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productDetailId, ["ProductId"] = request.ProductId, ["Description"] = request.Description, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductDetailResponse>> UpdateAsync(
        Guid productDetailId,
        UpdateProductDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productDetailId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductDetailResponse>> UpdateCoreAsync(
        Guid productDetailId,
        UpdateProductDetailRequest request,
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
                ProductDetailSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productDetailId, ["ProductId"] = request.ProductId, ["Description"] = request.Description, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productDetailId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productDetailId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductDetailResponse>> DeleteAsync(
        Guid productDetailId,
            DeleteProductDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productDetailId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductDetailResponse>> DeleteCoreAsync(
        Guid productDetailId,
            DeleteProductDetailRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productDetailId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                ProductDetailSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productDetailId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productDetailId,
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

    private static Result<ProductDetailResponse>? ValidateWriteRequest(
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

    private static Result<ProductDetailResponse> ValidationFailure(
        string field) =>
        Result<ProductDetailResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.product_items.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductDetailResponse>> ResolveWriteFailureAsync(
        Guid productDetailId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductDetailRecord>(
                ProductDetailSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productDetailId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductDetailResponse> VersionConflict() =>
        Result<ProductDetailResponse>.Failure(new Error(
            ProductDetailErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

    private static Result<ProductDetailResponse> NotFound() =>
        ProductDetailFeatureErrors.NotFound();
}

internal static class ProductDetailFeatureErrors
{
    internal static Result<ProductDetailResponse> NotFound() =>
        Result<ProductDetailResponse>.Failure(new Error(
            ProductDetailErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));
}
