#nullable enable
using Full.NET.Abstractions.Results;
namespace Acme.Remediation.Generated;
internal sealed class OrganizationLinkRelationLookupService(OrganizationLinkSourceQueryService source, OrganizationLinkTargetQueryService target)
{
public async Task<Result<PagedResult<OrganizationLinkRelationOption>>> ListSourceAsync(int page, int pageSize, string? search, Guid? selected, Guid actorUserId, bool isSuperAdministrator, Guid? organizationUnitId, CancellationToken token = default)
{

    var query = new OrganizationLinkSourceListQuery { FilterId = selected, FilterName = selected is not null || string.IsNullOrEmpty(search) ? null : search, FilterOrganizationUnitId = organizationUnitId };
    var result = await source.ListAsync(actorUserId, isSuperAdministrator, page, pageSize, token, query).ConfigureAwait(false);
    if (!result.IsSuccess) return Result<PagedResult<OrganizationLinkRelationOption>>.Failure(result.Error!);
    var value = result.Value!;
    return Result<PagedResult<OrganizationLinkRelationOption>>.Success(new(value.Items.Select(item => new OrganizationLinkRelationOption(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString("D") : item.Name)).ToArray(), value.Page, value.PageSize, value.Total));
}
public async Task<Result<PagedResult<OrganizationLinkRelationOption>>> ListTargetAsync(int page, int pageSize, string? search, Guid? selected, Guid actorUserId, bool isSuperAdministrator, Guid? organizationUnitId, CancellationToken token = default)
{

    var query = new OrganizationLinkTargetListQuery { FilterId = selected, FilterName = selected is not null || string.IsNullOrEmpty(search) ? null : search, FilterOrganizationUnitId = organizationUnitId };
    var result = await target.ListAsync(actorUserId, isSuperAdministrator, page, pageSize, token, query).ConfigureAwait(false);
    if (!result.IsSuccess) return Result<PagedResult<OrganizationLinkRelationOption>>.Failure(result.Error!);
    var value = result.Value!;
    return Result<PagedResult<OrganizationLinkRelationOption>>.Success(new(value.Items.Select(item => new OrganizationLinkRelationOption(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString("D") : item.Name)).ToArray(), value.Page, value.PageSize, value.Total));
}
}