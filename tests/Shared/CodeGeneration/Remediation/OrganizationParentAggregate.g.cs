#nullable enable
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
namespace Acme.Remediation.Generated;

internal sealed class OrganizationParentAggregateService(
    OrganizationParentManagementService management, OrganizationParentQueryService queries,
    OrganizationParentDetailManagementService details,
    IQueryExecutor queryExecutor, ICommandTransaction transaction, IOptions<DatabaseOptions> databaseOptions, ICurrentTenant currentTenant)
{
    internal static Error Invalid() => new("catalog.organization_parents.invalid_aggregate", "The aggregate request is invalid or exceeds 128 detail rows.", ErrorType.Validation);
    private static Result<OrganizationParentAggregateResponse> Failure(Error error) => Result<OrganizationParentAggregateResponse>.Failure(error);
    private static OrganizationParentDetailResponse Map(OrganizationParentDetailRecord record) => new(record.Id, record.TenantId, record.OrganizationUnitId, record.Name, record.ParentReferenceId, record.Version, record.IsDeleted, record.DeletedAtUtc, record.DeletedById);
    private static CreateOrganizationParentDetailRequest CreateDetail(OrganizationParentDetailInput input, OrganizationParentResponse master) => new(input.Name, master.Id);
    private static UpdateOrganizationParentDetailRequest UpdateDetail(OrganizationParentDetailInput input, OrganizationParentResponse master, long version) => new(input.Name, master.Id, version);

    public async Task<Result<OrganizationParentAggregateResponse>> GetAsync(Guid id, Guid actorUserId, bool isSuperAdministrator, CancellationToken token = default)
    {
        var master = await queries.GetByIdAsync(id, actorUserId, isSuperAdministrator, token).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var rows = await ReadDetailsAsync(master.Value!, token).ConfigureAwait(false);
        return rows.Count > 128 ? Failure(Invalid()) : Result<OrganizationParentAggregateResponse>.Success(new(master.Value!, rows));
    }

    public Task<Result<OrganizationParentAggregateResponse>> CreateAsync(CreateOrganizationParentAggregateRequest request, Guid actorUserId, Guid organizationUnitId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
    {
        if (request is null || request.Master is null || request.Details is null || request.Details.Count > 128 || request.Details.Any(item => item is null)) return Failure(Invalid());
        var master = await management.CreateAsync(request.Master, actorUserId, organizationUnitId, inner).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var saved = new List<OrganizationParentDetailResponse>();
        foreach (var input in request.Details)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, master.Value!.OrganizationUnitId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
            saved.Add(result.Value!);
        }
        return Result<OrganizationParentAggregateResponse>.Success(new(master.Value!, saved));
    }, token);

    public Task<Result<OrganizationParentAggregateResponse>> UpdateAsync(Guid id, UpdateOrganizationParentAggregateRequest request, Guid actorUserId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
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
            var result = await details.DeleteAsync(removal.Id, new DeleteOrganizationParentDetailRequest(removal.Version), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        foreach (var input in request.Added)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, master.Value!.OrganizationUnitId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        return Result<OrganizationParentAggregateResponse>.Success(new(master.Value!, await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false)));
    }, token);

    private async Task<IReadOnlyList<OrganizationParentDetailResponse>> ReadDetailsAsync(OrganizationParentResponse master, CancellationToken token)
    {
        var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? "SELECT Id, TenantId, OrganizationUnitId, Name, ParentId AS ParentReferenceId, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_organization_child WHERE ParentId = @ParentId AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND IsDeleted = 0 ORDER BY Id LIMIT 129"
            : "SELECT TOP (129) Id, TenantId, OrganizationUnitId, Name, ParentId AS ParentReferenceId, Version, IsDeleted, DeletedAtUtc, DeletedById FROM acme_catalog_organization_child WHERE ParentId = @ParentId AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND IsDeleted = 0 ORDER BY Id";
        var rows = await queryExecutor.QueryAsync<OrganizationParentDetailRecord>(new SqlStatement("catalog.organization_parent.aggregate_details", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["ParentId"] = master.Id, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = master.OrganizationUnitId }, token).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }
}