using Full.NET.Abstractions.Results;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Features.QueryHostEnumCatalogs;

/// <summary>Host 枚举/常量目录只读查询。</summary>
internal sealed class HostEnumCatalogQueryService(EnumCatalogRegistry registry)
{
    public Task<Result<IReadOnlyList<EnumCatalogSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = registry.List()
            .Select(catalog => new EnumCatalogSummary(
                catalog.Key,
                catalog.DisplayName,
                catalog.Description,
                catalog.Members.Count))
            .ToArray();
        return Task.FromResult(
            Result<IReadOnlyList<EnumCatalogSummary>>.Success(items));
    }

    public Task<Result<EnumCatalogDetail>> GetByKeyAsync(
        string catalogKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = registry.FindByKey(catalogKey);
        if (catalog is null)
        {
            return Task.FromResult(NotFound());
        }

        var members = catalog.Members
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .Select(item => new EnumCatalogMember(
                item.Code,
                item.Label,
                item.DisplayOrder))
            .ToArray();

        return Task.FromResult(
            Result<EnumCatalogDetail>.Success(
                new EnumCatalogDetail(
                    catalog.Key,
                    catalog.DisplayName,
                    catalog.Description,
                    members)));
    }

    private static Result<EnumCatalogDetail> NotFound() =>
        Result<EnumCatalogDetail>.Failure(new Error(
            SettingsErrorCodes.EnumCatalogNotFound,
            "The enumeration or constant catalog was not found.",
            ErrorType.NotFound));
}
