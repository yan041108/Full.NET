using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageUnitLeadership;

/// <summary>领导权配置校验在事务外读取 Identity；事务内仅锁定并修改 Organization 自有事实。</summary>
internal sealed class OrganizationUnitLeadershipService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    TenantUnitQueryService units, ITenantMemberSelectionDirectory members, ICurrentTenant tenant, IClock clock, IOptions<DatabaseOptions> database)
{
    public async Task<Result<OrganizationUnitLeadershipResponse>> GetAsync(Guid unitId, Guid actor, bool superAdministrator, CancellationToken token)
    {
        EnsureTenant();
        var visible = await units.GetByIdForActorAsync(unitId, actor, superAdministrator, token).ConfigureAwait(false);
        if (!visible.IsSuccess) return Result<OrganizationUnitLeadershipResponse>.Failure(visible.Error!);
        var row = await query.QuerySingleOrDefaultAsync<OrganizationUnitLeadershipRecord>(OrganizationUnitLeadershipSql.Find,
            P(("UnitId", unitId)), token).ConfigureAwait(false);
        TenantUserDirectoryEntry? selected = null;
        if (row?.LeaderUserId is { } leader)
            (await members.FindActiveTenantMembersAsync([leader], token).ConfigureAwait(false)).TryGetValue(leader, out selected);
        return Result<OrganizationUnitLeadershipResponse>.Success(Map(visible.Value!, row, DisplayName(selected)));
    }

    public async Task<Result<PagedResult<OrganizationUnitLeaderCandidateResponse>>> CandidatesAsync(Guid unitId, Guid actor, bool superAdministrator,
        int page, int pageSize, CancellationToken token)
    {
        EnsureTenant();
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Result<PagedResult<OrganizationUnitLeaderCandidateResponse>>.Failure(Invalid());
        var visible = await units.GetByIdForActorAsync(unitId, actor, superAdministrator, token).ConfigureAwait(false);
        if (!visible.IsSuccess) return Result<PagedResult<OrganizationUnitLeaderCandidateResponse>>.Failure(visible.Error!);
        if (!visible.Value!.IsActive) return Result<PagedResult<OrganizationUnitLeaderCandidateResponse>>.Failure(Conflict());
        var result = await members.ListActiveTenantMembersAsync(page, pageSize, token).ConfigureAwait(false);
        return Result<PagedResult<OrganizationUnitLeaderCandidateResponse>>.Success(new(result.Items.Select(member =>
            new OrganizationUnitLeaderCandidateResponse(member.Id, member.Username, member.DisplayName)).ToArray(), result.Page, result.PageSize, result.Total));
    }

    public async Task<Result<OrganizationUnitLeadershipResponse>> SetAsync(Guid unitId, Guid actor, bool superAdministrator,
        SetOrganizationUnitLeadershipRequest request, CancellationToken token)
    {
        EnsureTenant();
        if (unitId == Guid.Empty || actor == Guid.Empty || request.LeaderUserId == Guid.Empty || request.ExpectedRevision is < 0 or long.MaxValue || request.ExpectedUnitVersion < 1)
            return Result<OrganizationUnitLeadershipResponse>.Failure(Invalid());
        var visible = await units.GetByIdForActorAsync(unitId, actor, superAdministrator, token).ConfigureAwait(false);
        if (!visible.IsSuccess) return Result<OrganizationUnitLeadershipResponse>.Failure(visible.Error!);
        var unitContext = visible.Value!;
        if (!unitContext.IsActive || unitContext.Version != request.ExpectedUnitVersion)
            return Result<OrganizationUnitLeadershipResponse>.Failure(Conflict());
        TenantUserDirectoryEntry? selected = null;
        if (request.LeaderUserId is { } leader && !(await members.FindActiveTenantMembersAsync([leader], token).ConfigureAwait(false)).TryGetValue(leader, out selected))
            return Result<OrganizationUnitLeadershipResponse>.Failure(new(OrganizationErrorCodes.UnitLeaderInvalid, "The selected leader is not an active member of the current tenant.", ErrorType.Validation));
        var selectedName = DisplayName(selected);
        return await transaction.ExecuteResultAsync(async inner =>
        {
            var unit = await query.QuerySingleOrDefaultAsync<OrganizationLeadershipUnitRecord>(database.Value.Provider switch {
                DatabaseProvider.SqlServer => OrganizationUnitLeadershipSql.LockUnitSqlServer,
                DatabaseProvider.MySql => OrganizationUnitLeadershipSql.LockUnitMySql,
                _ => throw new InvalidOperationException("Unsupported database provider.")
            }, P(("UnitId", unitId)), inner).ConfigureAwait(false);
            if (unit is null || !unit.IsActive || unit.Version != request.ExpectedUnitVersion)
                return Result<OrganizationUnitLeadershipResponse>.Failure(Conflict());
            var previous = await query.QuerySingleOrDefaultAsync<OrganizationUnitLeadershipRecord>(OrganizationUnitLeadershipSql.Find, P(("UnitId", unitId)), inner).ConfigureAwait(false);
            if ((previous?.Revision ?? 0) != request.ExpectedRevision)
                return Result<OrganizationUnitLeadershipResponse>.Failure(Conflict());
            if (previous?.LeaderUserId == request.LeaderUserId)
                return Result<OrganizationUnitLeadershipResponse>.Success(Map(unitContext, previous, selectedName));
            var now = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
            var changed = await command.ExecuteAsync(previous is null ? OrganizationUnitLeadershipSql.Insert : OrganizationUnitLeadershipSql.Update,
                P(("UnitId", unitId), ("UnitVersion", unit.Version), ("LeaderUserId", request.LeaderUserId),
                    ("ActorUserId", actor), ("Revision", request.ExpectedRevision), ("Now", now)), inner).ConfigureAwait(false);
            if (changed != 1) return Result<OrganizationUnitLeadershipResponse>.Failure(Conflict());
            return Result<OrganizationUnitLeadershipResponse>.Success(new(unitId, unit.Version, true, request.LeaderUserId,
                request.ExpectedRevision + 1, actor, now, selectedName));
        }, token).ConfigureAwait(false);
    }

    private void EnsureTenant()
    {
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is null) throw new TenantContextMissingException("organization.tenant_context_required");
    }
    private static OrganizationUnitLeadershipResponse Map(OrganizationUnitResponse unit, OrganizationUnitLeadershipRecord? row, string? selectedName = null) =>
        new(unit.Id, unit.Version, unit.IsActive, row?.LeaderUserId, row?.Revision ?? 0, row?.UpdatedById, row?.UpdatedAtUtc, selectedName);
    private static string? DisplayName(TenantUserDirectoryEntry? member) => member is null ? null : string.IsNullOrWhiteSpace(member.DisplayName) ? member.Username : member.DisplayName;
    private static object P(params (string Name, object? Value)[] values) => OrganizationSqlParameters.Create(values);
    private static Error Invalid() => new(OrganizationErrorCodes.UnitLeadershipInvalid, "The leadership request is invalid.", ErrorType.Validation);
    private static Error Conflict() => new(OrganizationErrorCodes.UnitLeadershipConflict, "The unit or leadership configuration changed.", ErrorType.Conflict);
}
