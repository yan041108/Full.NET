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

internal sealed class ProductTagQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductTagResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductTagListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductTagResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductTagSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductTagSql.PageMySqlStatement,
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
                                .ReadAsync<ProductTagRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductTagResponse>>.Success(
                    new PagedResult<ProductTagResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductTagResponse>> GetByIdAsync(
                Guid productTagId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductTagRecord>(
                        ProductTagSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productTagId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductTagResponse>.Success(Map(record));
            }

        internal Task<Result<ProductTagResponse>> FindByIdAsync(
            Guid productTagId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productTagId, cancellationToken);

    private static ProductTagResponse Map(
        ProductTagRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.ProductReferenceId,
            record.TagId,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<ProductTagResponse> NotFound() =>
        ProductTagFeatureErrors.NotFound();

}

internal sealed class ProductTagManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductTagQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductTagResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductTagResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductTagResponse>.Failure(new Error(
                "catalog.product_tags.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductTagResponse>> CreateAsync(
        CreateProductTagRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductTagResponse>> CreateCoreAsync(
        CreateProductTagRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.ProductReferenceId,
            request.TagId);
        if (validationError is not null)
        {
            return validationError;
        }

        var productTagId = idGenerator.NewId();
        var removedPair = await queryExecutor.QuerySingleOrDefaultAsync<ProductTagRecord>(
            new SqlStatement("catalog.product_tag.removed_pair", "SELECT Id, TenantId, ProductId AS ProductReferenceId, TagId, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_product_tag WHERE ProductId = @ProductId AND TagId = @TagId AND TenantId = @TenantId AND IsDeleted = 1", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["ProductId"] = request.ProductReferenceId, ["TagId"] = request.TagId, ["TenantId"] = currentTenant.Id!.Value }, cancellationToken).ConfigureAwait(false);
        if (removedPair is not null) productTagId = removedPair.Id;

        var productIdPrincipalError = await EnsureProductIdPrincipalExistsAsync(
            request.ProductReferenceId,
            cancellationToken)
            .ConfigureAwait(false);
        if (productIdPrincipalError is not null)
        {
            return productIdPrincipalError;
        }

        var tagIdPrincipalError = await EnsureTagIdPrincipalExistsAsync(
            request.TagId,
            cancellationToken)
            .ConfigureAwait(false);
        if (tagIdPrincipalError is not null)
        {
            return tagIdPrincipalError;
        }

        if (removedPair is not null)
        {
            var restored = await commandExecutor.ExecuteAsync(new SqlStatement("catalog.product_tag.restore_pair",
                "UPDATE acme_catalog_product_tag SET IsDeleted = 0, Version = Version + 1, DeletedAtUtc = NULL, DeletedById = NULL, ProductId = @ProductId, TagId = @TagId WHERE Id = @Id AND IsDeleted = 1 AND TenantId = @TenantId AND Version = @Version", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Id"] = productTagId, ["TenantId"] = currentTenant.Id!.Value, ["Version"] = removedPair.Version, ["ProductId"] = request.ProductReferenceId, ["TagId"] = request.TagId }, cancellationToken).ConfigureAwait(false);
            if (restored != 1) return ProductTagFeatureErrors.UniqueConflict();
            return await queries.FindByIdAsync(productTagId, cancellationToken).ConfigureAwait(false);
        }
        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productTagId, ["ProductId"] = request.ProductReferenceId, ["TagId"] = request.TagId, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productTagId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductTagResponse>> UpdateAsync(
        Guid productTagId,
        UpdateProductTagRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productTagId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagResponse>> UpdateCoreAsync(
        Guid productTagId,
        UpdateProductTagRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.ProductReferenceId,
        request.TagId);
    if (validationError is not null)
    {
        return validationError;
    }

        var productIdPrincipalError = await EnsureProductIdPrincipalExistsAsync(
            request.ProductReferenceId,
            cancellationToken)
            .ConfigureAwait(false);
        if (productIdPrincipalError is not null)
        {
            return productIdPrincipalError;
        }

        var tagIdPrincipalError = await EnsureTagIdPrincipalExistsAsync(
            request.TagId,
            cancellationToken)
            .ConfigureAwait(false);
        if (tagIdPrincipalError is not null)
        {
            return tagIdPrincipalError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productTagId, ["ProductId"] = request.ProductReferenceId, ["TagId"] = request.TagId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productTagId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductTagResponse>> DeleteAsync(
        Guid productTagId,
            DeleteProductTagRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productTagId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagResponse>> DeleteCoreAsync(
        Guid productTagId,
            DeleteProductTagRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productTagId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productTagId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagId,
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

    private static Result<ProductTagResponse>? ValidateWriteRequest(
        Guid productReferenceId,
        Guid tagId)
    {
        if (productReferenceId == Guid.Empty)
        {
            return ValidationFailure("ProductReferenceId");
        }

        if (tagId == Guid.Empty)
        {
            return ValidationFailure("TagId");
        }
        return null;
    }

    private static Result<ProductTagResponse> ValidationFailure(
        string field) =>
        Result<ProductTagResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.product_tags.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductTagResponse>> ResolveWriteFailureAsync(
        Guid productTagId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductTagRecord>(
                ProductTagSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productTagId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductTagResponse> VersionConflict() =>
        Result<ProductTagResponse>.Failure(new Error(
            ProductTagErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task<Result<ProductTagResponse>?> EnsureProductIdPrincipalExistsAsync(
    Guid principalId,
    CancellationToken cancellationToken)
{

    var found = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_product_for_product_tag_productId",
                "SELECT Id FROM acme_catalog_product WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Id"] = principalId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return found is null
        ? ProductTagFeatureErrors.PrincipalNotFound()
        : null;
}

private async Task<Result<ProductTagResponse>?> EnsureTagIdPrincipalExistsAsync(
    Guid principalId,
    CancellationToken cancellationToken)
{

    var found = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_tag_for_product_tag_tagId",
                "SELECT Id FROM acme_catalog_tag WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Id"] = principalId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return found is null
        ? ProductTagFeatureErrors.PrincipalNotFound()
        : null;
}

    private static Result<ProductTagResponse> NotFound() =>
        ProductTagFeatureErrors.NotFound();
}

internal static class ProductTagFeatureErrors
{
    internal static Result<ProductTagResponse> NotFound() =>
        Result<ProductTagResponse>.Failure(new Error(
            ProductTagErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<ProductTagResponse> UniqueConflict() =>
    Result<ProductTagResponse>.Failure(new Error(
        ProductTagErrorCodes.UniqueConflict,
        "The unique field value already exists.",
        ErrorType.Conflict));

internal static Result<ProductTagResponse> PrincipalNotFound() =>
    Result<ProductTagResponse>.Failure(new Error(
        ProductTagErrorCodes.PrincipalNotFound,
        "The related principal was not found in the current module.",
        ErrorType.Validation));
}
