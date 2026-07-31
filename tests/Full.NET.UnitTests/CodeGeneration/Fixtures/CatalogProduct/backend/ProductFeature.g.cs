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
using Microsoft.Extensions.Options;

namespace Acme.Modules.Catalog.Generated;

internal sealed class ProductQueryService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<ProductResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
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
                new { Offset = offset, PageSize = pageSize },
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
                new { Id = productId },
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
            record.Description,
            record.IsActive,
            record.Version,
            record.CreatedAtUtc);

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
    public Task<Result<ProductResponse>> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<ProductResponse>> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(productId, request, token),
            cancellationToken);

    public Task<Result<ProductResponse>> DisableAsync(
        Guid productId,
        DisableProductRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(
                productId, request,
                token),
            cancellationToken);

    private async Task<Result<ProductResponse>> CreateCoreAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name,
            request.Description);
        if (validationError is not null)
        {
            return validationError;
        }

        var productId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.InsertStatement,
                new
                {
                    Id = productId,
                    request.Name,
                    request.Description,
                    request.IsActive,
                    Version = 1L,
                    CreatedAtUtc = clock.UtcNow
                },
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

    private async Task<Result<ProductResponse>> UpdateCoreAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(
            request.Name,
            request.Description);
        if (validationError is not null)
        {
            return validationError;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.UpdateStatement,
                new
                {
                    Id = productId,
                    request.Name,
                    request.Description,
                    request.IsActive,
                    request.Version
                },
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

    private async Task<Result<ProductResponse>> DisableCoreAsync(
        Guid productId,
        DisableProductRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var affectedRows = await commandExecutor.ExecuteAsync(
                ProductSql.DisableStatement,
                new
                {
                    Id = productId,
                    request.Version
                },
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
        string? name,
        string? description)
    {
        if (name is null || name.Length > 200)
        {
            return ValidationFailure("Name");
        }

        if (description is { Length: > 500 })
        {
            return ValidationFailure("Description");
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
            }));

    private async Task<Result<ProductResponse>> ResolveWriteFailureAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ProductRecord>(
                ProductSql.FindByIdStatement,
                new { Id = productId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private static Result<ProductResponse> VersionConflict() =>
        Result<ProductResponse>.Failure(new Error(
            ProductErrorCodes.VersionConflict,
            "The resource was updated concurrently.",
            ErrorType.Conflict));

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
}
