#nullable enable
using Full.NET.Abstractions.Results;
namespace Acme.Remediation.Generated;
internal sealed class ProductTagRelationLookupService(ProductTagSourceQueryService source, ProductTagTargetQueryService target)
{
public async Task<Result<PagedResult<ProductTagRelationOption>>> ListSourceAsync(int page, int pageSize, string? search = null, Guid? selected = null, CancellationToken token = default)
{

    var query = new ProductTagSourceListQuery { FilterId = selected, FilterName = selected is not null || string.IsNullOrEmpty(search) ? null : search };
    var result = await source.ListAsync(page, pageSize, token, query).ConfigureAwait(false);
    if (!result.IsSuccess) return Result<PagedResult<ProductTagRelationOption>>.Failure(result.Error!);
    var value = result.Value!;
    return Result<PagedResult<ProductTagRelationOption>>.Success(new(value.Items.Select(item => new ProductTagRelationOption(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString("D") : item.Name)).ToArray(), value.Page, value.PageSize, value.Total));
}
public async Task<Result<PagedResult<ProductTagRelationOption>>> ListTargetAsync(int page, int pageSize, string? search = null, Guid? selected = null, CancellationToken token = default)
{

    var query = new ProductTagTargetListQuery { FilterId = selected, FilterName = selected is not null || string.IsNullOrEmpty(search) ? null : search };
    var result = await target.ListAsync(page, pageSize, token, query).ConfigureAwait(false);
    if (!result.IsSuccess) return Result<PagedResult<ProductTagRelationOption>>.Failure(result.Error!);
    var value = result.Value!;
    return Result<PagedResult<ProductTagRelationOption>>.Success(new(value.Items.Select(item => new ProductTagRelationOption(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString("D") : item.Name)).ToArray(), value.Page, value.PageSize, value.Total));
}
}