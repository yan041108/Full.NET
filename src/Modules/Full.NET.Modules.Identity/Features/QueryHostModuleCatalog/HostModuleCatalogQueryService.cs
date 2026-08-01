using Full.NET.Abstractions.Results;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.QueryHostModuleCatalog;

/// <summary>Host 只读模块清单查询；数据来自 Composition 物化的不可变快照。</summary>
internal sealed class HostModuleCatalogQueryService(IFullNetModuleCatalog catalog)
{
    public Task<Result<IReadOnlyList<ModuleCatalogEntryResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = catalog.List()
            .Select(ToResponse)
            .ToArray();
        return Task.FromResult(
            Result<IReadOnlyList<ModuleCatalogEntryResponse>>.Success(items));
    }

    public Task<Result<ModuleCatalogEntryResponse>> GetByKeyAsync(
        string moduleKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = catalog.FindByKey(moduleKey);
        if (descriptor is null)
        {
            return Task.FromResult(NotFound());
        }

        return Task.FromResult(
            Result<ModuleCatalogEntryResponse>.Success(ToResponse(descriptor)));
    }

    private static ModuleCatalogEntryResponse ToResponse(FullNetModuleDescriptor descriptor) =>
        new(
            descriptor.ModuleKey,
            descriptor.DisplayName,
            descriptor.Version,
            descriptor.Dependencies,
            descriptor.HostProfiles,
            descriptor.SourceClassification.ToString(),
            descriptor.HealthCapability.ToString());

    private static Result<ModuleCatalogEntryResponse> NotFound() =>
        Result<ModuleCatalogEntryResponse>.Failure(new Error(
            IdentityErrorCodes.ModuleCatalogNotFound,
            "The module was not found in the read-only catalog.",
            ErrorType.NotFound));
}
