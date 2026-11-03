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

internal sealed class ProductQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductSql.PageMySqlStatement,
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
                                .ReadAsync<ProductRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductResponse>>.Success(
                    new PagedResult<ProductResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductResponse>> GetByIdAsync(
                Guid productId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductRecord>(
                        ProductSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductResponse>.Success(Map(record));
            }

        internal Task<Result<ProductResponse>> FindByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productId, cancellationToken);

    private static ProductResponse Map(
        ProductRecord record) =>
        new(
            record.Id,
            record.TenantId,
            record.Name,
            record.Number,
            record.Price,
            record.OptionalCode,
            record.Version,
            record.IsDeleted,
            record.DeletedAtUtc,
            record.DeletedById);

    private static Result<ProductResponse> NotFound() =>
        ProductFeatureErrors.NotFound();

}

internal sealed class ProductManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductResponse>.Failure(new Error(
                "catalog.products.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductResponse>> CreateAsync(
        CreateProductRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductResponse>> CreateCoreAsync(
        CreateProductRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name,
            request.Price,
            request.OptionalCode);
        if (validationError is not null)
        {
            return validationError;
        }

        var productId = idGenerator.NewId();

        var nameUniqueError = await EnsureUniqueNameAsync(
            productId,
            request.Name,
            cancellationToken)
            .ConfigureAwait(false);
        if (nameUniqueError is not null)
        {
            return nameUniqueError;
        }

        var numberUniqueError = await EnsureUniqueNumberAsync(
            productId,
            request.Number,
            cancellationToken)
            .ConfigureAwait(false);
        if (numberUniqueError is not null)
        {
            return numberUniqueError;
        }

        var optionalCodeUniqueError = await EnsureUniqueOptionalCodeAsync(
            productId,
            request.OptionalCode,
            cancellationToken)
            .ConfigureAwait(false);
        if (optionalCodeUniqueError is not null)
        {
            return optionalCodeUniqueError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productId, ["Name"] = request.Name, ["Number"] = request.Number, ["Price"] = request.Price, ["OptionalCode"] = request.OptionalCode, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductResponse>> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductResponse>> UpdateCoreAsync(
        Guid productId,
        UpdateProductRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
    var validationError = ValidateWriteRequest(
        request.Name,
        request.Price,
        request.OptionalCode);
    if (validationError is not null)
    {
        return validationError;
    }

        var nameUniqueError = await EnsureUniqueNameAsync(
            productId,
            request.Name,
            cancellationToken)
            .ConfigureAwait(false);
        if (nameUniqueError is not null)
        {
            return nameUniqueError;
        }

        var numberUniqueError = await EnsureUniqueNumberAsync(
            productId,
            request.Number,
            cancellationToken)
            .ConfigureAwait(false);
        if (numberUniqueError is not null)
        {
            return numberUniqueError;
        }

        var optionalCodeUniqueError = await EnsureUniqueOptionalCodeAsync(
            productId,
            request.OptionalCode,
            cancellationToken)
            .ConfigureAwait(false);
        if (optionalCodeUniqueError is not null)
        {
            return optionalCodeUniqueError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productId, ["Name"] = request.Name, ["Number"] = request.Number, ["Price"] = request.Price, ["OptionalCode"] = request.OptionalCode, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductResponse>> DeleteAsync(
        Guid productId,
            DeleteProductRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductResponse>> DeleteCoreAsync(
        Guid productId,
            DeleteProductRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


            await CascadeDeleteDependentsAsync(
            existing.Value!,
            actorUserId,
            cancellationToken)
            .ConfigureAwait(false);
                var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productId,
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

    private static Result<ProductResponse>? ValidateWriteRequest(
        string name,
        decimal price,
        string? optionalCode)
    {
        if (string.IsNullOrWhiteSpace(name) || name is { Length: > 64 })
        {
            return ValidationFailure("Name");
        }

        if ((price is decimal priceValue && (decimal.Round(priceValue, 2) != priceValue || priceValue <= -1000000m || priceValue >= 1000000m)))
        {
            return ValidationFailure("Price");
        }

        if (optionalCode is { Length: > 64 })
        {
            return ValidationFailure("OptionalCode");
        }
        return null;
    }

    private static Result<ProductResponse> ValidationFailure(
        string field) =>
        Result<ProductResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.products.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductResponse>> ResolveWriteFailureAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductRecord>(
                ProductSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductResponse> VersionConflict() =>
        Result<ProductResponse>.Failure(new Error(
            ProductErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task<Result<ProductResponse>?> EnsureUniqueNameAsync(
    Guid productId,
    string value,
    CancellationToken cancellationToken)
{

    var existingId = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_product_by_name",
                "SELECT Id FROM acme_catalog_product WHERE Name = @Value AND Id <> @Id AND TenantId = @TenantId",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductFeatureErrors.UniqueConflict();
}

private async Task<Result<ProductResponse>?> EnsureUniqueNumberAsync(
    Guid productId,
    int value,
    CancellationToken cancellationToken)
{

    var existingId = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_product_by_number",
                "SELECT Id FROM acme_catalog_product WHERE Number = @Value AND Id <> @Id AND TenantId = @TenantId",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductFeatureErrors.UniqueConflict();
}

private async Task<Result<ProductResponse>?> EnsureUniqueOptionalCodeAsync(
    Guid productId,
    string? value,
    CancellationToken cancellationToken)
{
    if (value is null) return null;
    var existingId = await queryExecutor
        .QuerySingleOrDefaultAsync<Guid?>(
            new SqlStatement(
                "catalog.find_product_by_optionalCode",
                "SELECT Id FROM acme_catalog_product WHERE OptionalCode = @Value AND Id <> @Id AND TenantId = @TenantId",
                SqlDataScope.TenantRequired,
                SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductFeatureErrors.UniqueConflict();
}

                private async Task CascadeDeleteDependentsAsync(
                    ProductResponse principal,
                    Guid actorUserId,
                    CancellationToken cancellationToken)
                {
        await commandExecutor.ExecuteAsync(
                new SqlStatement(
                    "catalog.cascade_delete_product_item_productId",
                    "UPDATE acme_catalog_product_item SET IsDeleted = 1, DeletedAtUtc = @DeletedAtUtc, DeletedById = @DeletedById, Version = Version + 1 WHERE ProductId = @Id AND TenantId = @TenantId AND IsDeleted = 0",
                    SqlDataScope.TenantRequired,
                    SqlTenantBinding.CurrentTenantId),
                new Dictionary<string, object?> { ["Id"] = principal.Id, ["TenantId"] = currentTenant.Id!.Value, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId },
                cancellationToken)
            .ConfigureAwait(false);
                }

    private static Result<ProductResponse> NotFound() =>
        ProductFeatureErrors.NotFound();
}

internal static class ProductFeatureErrors
{
    internal static Result<ProductResponse> NotFound() =>
        Result<ProductResponse>.Failure(new Error(
            ProductErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<ProductResponse> UniqueConflict() =>
    Result<ProductResponse>.Failure(new Error(
        ProductErrorCodes.UniqueConflict,
        "The unique field value already exists.",
        ErrorType.Conflict));
}
