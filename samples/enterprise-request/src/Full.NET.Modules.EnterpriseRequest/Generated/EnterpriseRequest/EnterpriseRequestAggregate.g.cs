#nullable enable
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal sealed class EnterpriseRequestAggregateService(
    EnterpriseRequestManagementService management, EnterpriseRequestQueryService queries,
    EnterpriseRequestDetailManagementService details,
    IQueryExecutor queryExecutor, ICommandTransaction transaction, IOptions<DatabaseOptions> databaseOptions, ICurrentTenant currentTenant)
{
    internal static Error Invalid() => new("enterprise_request.enterprise_requests.invalid_aggregate", "The aggregate request is invalid or exceeds 128 detail rows.", ErrorType.Validation);
    private static Result<EnterpriseRequestAggregateResponse> Failure(Error error) => Result<EnterpriseRequestAggregateResponse>.Failure(error);
    private static EnterpriseRequestDetailResponse Map(EnterpriseRequestDetailRecord record) => new(record.Id, record.TenantId, record.OrganizationUnitId, record.RequestId, record.LineNumber, record.ItemDescription, record.Quantity, record.UnitPrice, record.LineAmount, record.Version, record.CreatedAtUtc, record.CreatedById, record.UpdatedAtUtc, record.UpdatedById, record.IsDeleted, record.DeletedAtUtc, record.DeletedById);
    private static CreateEnterpriseRequestDetailRequest CreateDetail(EnterpriseRequestDetailInput input, EnterpriseRequestResponse master) => new(master.Id, input.LineNumber, input.ItemDescription, input.Quantity, input.UnitPrice, input.LineAmount);
    private static UpdateEnterpriseRequestDetailRequest UpdateDetail(EnterpriseRequestDetailInput input, EnterpriseRequestResponse master, long version) => new(master.Id, input.LineNumber, input.ItemDescription, input.Quantity, input.UnitPrice, input.LineAmount, version);

    public async Task<Result<EnterpriseRequestAggregateResponse>> GetAsync(Guid id, Guid actorUserId, bool isSuperAdministrator, CancellationToken token = default)
    {
        var master = await queries.GetByIdAsync(id, actorUserId, isSuperAdministrator, token).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var rows = await ReadDetailsAsync(master.Value!, token).ConfigureAwait(false);
        return rows.Count > 128 ? Failure(Invalid()) : Result<EnterpriseRequestAggregateResponse>.Success(new(master.Value!, rows));
    }

    public Task<Result<EnterpriseRequestAggregateResponse>> CreateAsync(CreateEnterpriseRequestAggregateRequest request, Guid actorUserId, Guid organizationUnitId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
    {
        if (request is null || request.Master is null || request.Details is null || request.Details.Count > 128 || request.Details.Any(item => item is null)) return Failure(Invalid());
        var master = await management.CreateAsync(request.Master, actorUserId, organizationUnitId, inner).ConfigureAwait(false);
        if (!master.IsSuccess) return Failure(master.Error!);
        var saved = new List<EnterpriseRequestDetailResponse>();
        foreach (var input in request.Details)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, master.Value!.OrganizationUnitId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
            saved.Add(result.Value!);
        }
        return Result<EnterpriseRequestAggregateResponse>.Success(new(master.Value!, saved));
    }, token);

    public Task<Result<EnterpriseRequestAggregateResponse>> UpdateAsync(Guid id, UpdateEnterpriseRequestAggregateRequest request, Guid actorUserId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
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
            var result = await details.DeleteAsync(removal.Id, new DeleteEnterpriseRequestDetailRequest(removal.Version), actorUserId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        foreach (var input in request.Added)
        {
            var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId, master.Value!.OrganizationUnitId, inner).ConfigureAwait(false);
            if (!result.IsSuccess) return Failure(result.Error!);
        }
        return Result<EnterpriseRequestAggregateResponse>.Success(new(master.Value!, await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false)));
    }, token);

    private async Task<IReadOnlyList<EnterpriseRequestDetailResponse>> ReadDetailsAsync(EnterpriseRequestResponse master, CancellationToken token)
    {
        var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? "SELECT Id, TenantId, OrganizationUnitId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount, Version, CreatedAtUtc, CreatedById, UpdatedAtUtc, UpdatedById, IsDeleted, DeletedAtUtc, DeletedById FROM demo_enterprise_request_enterprise_request_line WHERE RequestId = @ParentId AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND IsDeleted = 0 ORDER BY Id LIMIT 129"
            : "SELECT TOP (129) Id, TenantId, OrganizationUnitId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount, Version, CreatedAtUtc, CreatedById, UpdatedAtUtc, UpdatedById, IsDeleted, DeletedAtUtc, DeletedById FROM demo_enterprise_request_enterprise_request_line WHERE RequestId = @ParentId AND TenantId = @TenantId AND OrganizationUnitId = @OrganizationUnitId AND IsDeleted = 0 ORDER BY Id";
        var rows = await queryExecutor.QueryAsync<EnterpriseRequestDetailRecord>(new SqlStatement("enterprise_request.enterprise_request.aggregate_details", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["ParentId"] = master.Id, ["TenantId"] = currentTenant.Id!.Value, ["OrganizationUnitId"] = master.OrganizationUnitId }, token).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }
}