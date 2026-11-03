#nullable enable
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
namespace Acme.Remediation.Generated;

internal sealed class ProductAggregateService(
    ProductManagementService management, ProductQueryService queries,
    ProductDetailManagementService details,
    IQueryExecutor queryExecutor, ICommandTransaction transaction, IOptions<DatabaseOptions> databaseOptions, ICurrentTenant currentTenant)
{
    internal static Error Invalid() => new("catalog.products.invalid_aggregate", "The aggregate request is invalid or exceeds 128 detail rows.", ErrorType.Validation);
    private static Result<ProductAggregateResponse> Failure(Error error) => Result<ProductAggregateResponse>.Failure(error);
    private static ProductDetailResponse Map(ProductDetailRecord record) => new(record.Id, record.TenantId, record.ProductId, record.Description, record.Version, record.IsDeleted, record.DeletedAtUtc, record.DeletedById);
    private static CreateProductDetailRequest CreateDetail(ProductDetailInput input, ProductResponse master) => new(master.Id, input.Description);
    private static UpdateProductDetailRequest UpdateDetail(ProductDetailInput input, ProductResponse master, long version) => new(master.Id, input.Description, version);

    public async Task<Result<ProductAggregateResponse>> GetAsync(Guid id, CancellationToken token = default)
    {
        var master = await queries.GetByIdAsync(id, token).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var rows = await ReadDetailsAsync(master.Value!, token).ConfigureAwait(false);
        return rows.Count > 128 ? Failure(Invalid()) : Result<ProductAggregateResponse>.Success(new(master.Value!, rows));
    }

    public Task<Result<ProductAggregateResponse>> CreateAsync(CreateProductAggregateRequest request, Guid actorUserId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
    {
        if (request is null || request.Master is null || request.Details is null || request.Details.Count > 128 || request.Details.Any(item => item is null)) return Failure(Invalid());
        var master = await management.CreateAsync(request.Master, actorUserId, inner).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var saved = new List<ProductDetailResponse>();
        foreach (var input in request.Details)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
            saved.Add(result.Value!);
        }
        return Result<ProductAggregateResponse>.Success(new(master.Value!, saved));
    }, token);

    public Task<Result<ProductAggregateResponse>> UpdateAsync(Guid id, UpdateProductAggregateRequest request, Guid actorUserId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
    {
        if (request is null || request.Master is null || request.Added is null || request.Updated is null || request.Removed is null
            || request.Added.Count + request.Updated.Count + request.Removed.Count > 256
            || request.Added.Any(item => item is null) || request.Updated.Any(item => item is null || item.Input is null) || request.Removed.Any(item => item is null)) return Failure(Invalid());
        // 主端 Version 更新先取得行锁；从端任何失败会把整个 Result 事务回滚。
        var master = await management.UpdateAsync(id, request.Master, actorUserId, inner).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var existing = await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false);
        var touched = request.Updated.Select(item => item.Id).Concat(request.Removed.Select(item => item.Id)).ToArray();
        if (existing.Count > 128 || existing.Count + request.Added.Count - request.Removed.Count > 128
            || touched.Distinct().Count() != touched.Length || touched.Any(key => existing.All(item => item.Id != key))) return Failure(Invalid());
        foreach (var edit in request.Updated)
        {
            var result = await details.UpdateAsync(edit.Id, UpdateDetail(edit.Input, master.Value!, edit.Version), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        foreach (var removal in request.Removed)
        {
            var result = await details.DeleteAsync(removal.Id, new DeleteProductDetailRequest(removal.Version), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        foreach (var input in request.Added)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        return Result<ProductAggregateResponse>.Success(new(master.Value!, await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false)));
    }, token);

    private async Task<IReadOnlyList<ProductDetailResponse>> ReadDetailsAsync(ProductResponse master, CancellationToken token)
    {
        var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? "SELECT Id, TenantId, ProductId, Description, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_product_item WHERE ProductId = @ParentId AND TenantId = @TenantId AND IsDeleted = 0 ORDER BY Id LIMIT 129"
            : "SELECT TOP (129) Id, TenantId, ProductId, Description, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_product_item WHERE ProductId = @ParentId AND TenantId = @TenantId AND IsDeleted = 0 ORDER BY Id";
        var rows = await queryExecutor.QueryAsync<ProductDetailRecord>(new SqlStatement("catalog.product.aggregate_details", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["ParentId"] = master.Id, ["TenantId"] = currentTenant.Id!.Value }, token).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }
}