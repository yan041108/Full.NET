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

internal sealed class ProductTagSourceQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{

            public async Task<Result<PagedResult<ProductTagSourceResponse>>> ListAsync(
                int page,
                int pageSize,
                CancellationToken cancellationToken = default,
                ProductTagSourceListQuery? query = null)
            {
                    query ??= new();
                if (query.Validate() is { } queryError)
                    return Result<PagedResult<ProductTagSourceResponse>>.Failure(queryError);
                page = Math.Max(page, 1);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var offset = (long)(page - 1) * pageSize;
                var statement = databaseOptions.Value.Provider switch
                {
                    DatabaseProvider.SqlServer =>
                        ProductTagSourceSql.PageSqlServerStatement,
                    DatabaseProvider.MySql =>
                        ProductTagSourceSql.PageMySqlStatement,
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
                                .ReadAsync<ProductTagSourceRecord>()
                                .ConfigureAwait(false);
                            return (Total: total, Rows: rows);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<PagedResult<ProductTagSourceResponse>>.Success(
                    new PagedResult<ProductTagSourceResponse>(
                        pageResult.Rows.Select(Map).ToArray(),
                        page,
                        pageSize,
                        pageResult.Total));
            }

            public async Task<Result<ProductTagSourceResponse>> GetByIdAsync(
                Guid productTagSourceId,
                CancellationToken cancellationToken = default)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<ProductTagSourceRecord>(
                        ProductTagSourceSql.FindByIdStatement,
                        new Dictionary<string, object?> { ["Id"] = productTagSourceId },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? NotFound()
                    : Result<ProductTagSourceResponse>.Success(Map(record));
            }

        internal Task<Result<ProductTagSourceResponse>> FindByIdAsync(
            Guid productTagSourceId,
            CancellationToken cancellationToken = default) =>
            GetByIdAsync(productTagSourceId, cancellationToken);

    private static ProductTagSourceResponse Map(
        ProductTagSourceRecord record) =>
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

    private static Result<ProductTagSourceResponse> NotFound() =>
        ProductTagSourceFeatureErrors.NotFound();

}

internal sealed class ProductTagSourceManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ProductTagSourceQueryService queries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private async Task<Result<ProductTagSourceResponse>> ExecuteWriteAsync(
        Func<CancellationToken, Task<Result<ProductTagSourceResponse>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteResultAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<ProductTagSourceResponse>.Failure(new Error(
                "catalog.products.unique_conflict",
                "The unique field value already exists.", ErrorType.Conflict));
        }
    }
    public Task<Result<ProductTagSourceResponse>> CreateAsync(
        CreateProductTagSourceRequest request,
Guid actorUserId,
CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => CreateCoreAsync(
                request,
                actorUserId,

                token),
            cancellationToken);

    private async Task<Result<ProductTagSourceResponse>> CreateCoreAsync(
        CreateProductTagSourceRequest request,
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

        var productTagSourceId = idGenerator.NewId();

        var nameUniqueError = await EnsureUniqueNameAsync(
            productTagSourceId,
            request.Name,
            cancellationToken)
            .ConfigureAwait(false);
        if (nameUniqueError is not null)
        {
            return nameUniqueError;
        }

        var numberUniqueError = await EnsureUniqueNumberAsync(
            productTagSourceId,
            request.Number,
            cancellationToken)
            .ConfigureAwait(false);
        if (numberUniqueError is not null)
        {
            return numberUniqueError;
        }

        var optionalCodeUniqueError = await EnsureUniqueOptionalCodeAsync(
            productTagSourceId,
            request.OptionalCode,
            cancellationToken)
            .ConfigureAwait(false);
        if (optionalCodeUniqueError is not null)
        {
            return optionalCodeUniqueError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSourceSql.InsertStatement,
                new Dictionary<string, object?> { ["Id"] = productTagSourceId, ["Name"] = request.Name, ["Number"] = request.Number, ["Price"] = request.Price, ["OptionalCode"] = request.OptionalCode, ["Version"] = 1L, ["IsDeleted"] = false, ["DeletedAtUtc"] = (DateTimeOffset?)null, ["DeletedById"] = (Guid?)null },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The generated insert must affect exactly one row.");
        }

        return await queries.FindByIdAsync(
                productTagSourceId,
                cancellationToken)
            .ConfigureAwait(false);
    }


    public Task<Result<ProductTagSourceResponse>> UpdateAsync(
        Guid productTagSourceId,
        UpdateProductTagSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => UpdateCoreAsync(
                productTagSourceId,
                request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagSourceResponse>> UpdateCoreAsync(
        Guid productTagSourceId,
        UpdateProductTagSourceRequest request,
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
            productTagSourceId,
            request.Name,
            cancellationToken)
            .ConfigureAwait(false);
        if (nameUniqueError is not null)
        {
            return nameUniqueError;
        }

        var numberUniqueError = await EnsureUniqueNumberAsync(
            productTagSourceId,
            request.Number,
            cancellationToken)
            .ConfigureAwait(false);
        if (numberUniqueError is not null)
        {
            return numberUniqueError;
        }

        var optionalCodeUniqueError = await EnsureUniqueOptionalCodeAsync(
            productTagSourceId,
            request.OptionalCode,
            cancellationToken)
            .ConfigureAwait(false);
        if (optionalCodeUniqueError is not null)
        {
            return optionalCodeUniqueError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSourceSql.UpdateStatement,
                new Dictionary<string, object?> { ["Id"] = productTagSourceId, ["Name"] = request.Name, ["Number"] = request.Number, ["Price"] = request.Price, ["OptionalCode"] = request.OptionalCode, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagSourceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.FindByIdAsync(
                productTagSourceId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<ProductTagSourceResponse>> DeleteAsync(
        Guid productTagSourceId,
            DeleteProductTagSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            token => DeleteCoreAsync(
                productTagSourceId, request,
                actorUserId,
                token),
            cancellationToken);

    private async Task<Result<ProductTagSourceResponse>> DeleteCoreAsync(
        Guid productTagSourceId,
            DeleteProductTagSourceRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var existing = await queries.FindByIdAsync(
                productTagSourceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }


                    var affectedRows = await commandExecutor.ExecuteAsync(
                ProductTagSourceSql.DeleteStatement,
                new Dictionary<string, object?> { ["Id"] = productTagSourceId, ["DeletedAtUtc"] = clock.UtcNow, ["DeletedById"] = actorUserId, ["Version"] = request.Version },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveWriteFailureAsync(
                    productTagSourceId,
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

    private static Result<ProductTagSourceResponse>? ValidateWriteRequest(
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

    private static Result<ProductTagSourceResponse> ValidationFailure(
        string field) =>
        Result<ProductTagSourceResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "One or more generated field constraints were not satisfied.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["The field value is invalid."],
            }, null, new[] { new ValidationViolation(field, "catalog.products.invalid_field", new Dictionary<string, object?>()) }));

    private async Task<Result<ProductTagSourceResponse>> ResolveWriteFailureAsync(
        Guid productTagSourceId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductTagSourceRecord>(
                ProductTagSourceSql.FindByIdStatement,
                new Dictionary<string, object?> { ["Id"] = productTagSourceId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductTagSourceResponse> VersionConflict() =>
        Result<ProductTagSourceResponse>.Failure(new Error(
            ProductTagSourceErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));
private async Task<Result<ProductTagSourceResponse>?> EnsureUniqueNameAsync(
    Guid productTagSourceId,
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
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productTagSourceId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductTagSourceFeatureErrors.UniqueConflict();
}

private async Task<Result<ProductTagSourceResponse>?> EnsureUniqueNumberAsync(
    Guid productTagSourceId,
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
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productTagSourceId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductTagSourceFeatureErrors.UniqueConflict();
}

private async Task<Result<ProductTagSourceResponse>?> EnsureUniqueOptionalCodeAsync(
    Guid productTagSourceId,
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
            new Dictionary<string, object?> { ["Value"] = value, ["Id"] = productTagSourceId, ["TenantId"] = currentTenant.Id!.Value },
            cancellationToken)
        .ConfigureAwait(false);
    return existingId is null
        ? null
        : ProductTagSourceFeatureErrors.UniqueConflict();
}

    private static Result<ProductTagSourceResponse> NotFound() =>
        ProductTagSourceFeatureErrors.NotFound();
}

internal static class ProductTagSourceFeatureErrors
{
    internal static Result<ProductTagSourceResponse> NotFound() =>
        Result<ProductTagSourceResponse>.Failure(new Error(
            ProductTagSourceErrorCodes.NotFound,
            "The resource was not found.",
            ErrorType.NotFound));

internal static Result<ProductTagSourceResponse> UniqueConflict() =>
    Result<ProductTagSourceResponse>.Failure(new Error(
        ProductTagSourceErrorCodes.UniqueConflict,
        "The unique field value already exists.",
        ErrorType.Conflict));
}
