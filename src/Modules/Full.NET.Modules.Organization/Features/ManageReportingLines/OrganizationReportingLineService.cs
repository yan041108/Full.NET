using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageReportingLines;

/// <summary>直属汇报关系使用全组织数据范围；跨模块身份准备在外，环检测和关系写入在自有串行事务内。</summary>
internal sealed class OrganizationReportingLineService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    ITenantMemberSelectionDirectory members, IUserDataScopeResolver scopes, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    internal const int MaximumReportingDepth = 64;

    public async Task<Result<OrganizationReportingLineResponse>> GetAsync(Guid employee, Guid actor, bool superAdministrator, CancellationToken token)
    {
        if (!await HasScopeAsync(actor, superAdministrator, token).ConfigureAwait(false)) return Result<OrganizationReportingLineResponse>.Failure(ScopeDenied());
        if (employee == Guid.Empty) return Result<OrganizationReportingLineResponse>.Failure(Invalid());
        var row = await FindAsync(employee, token).ConfigureAwait(false);
        var requested = row?.ManagerUserId is { } manager ? new[] { employee, manager } : [employee];
        var active = await members.FindActiveTenantMembersAsync(requested.Distinct().ToArray(), token).ConfigureAwait(false);
        if (!active.ContainsKey(employee)) return Result<OrganizationReportingLineResponse>.Failure(NotFound());
        return Result<OrganizationReportingLineResponse>.Success(Map(employee, row, Name(active, row?.ManagerUserId)));
    }

    public async Task<Result<PagedResult<OrganizationReportingCandidate>>> CandidatesAsync(Guid employee, Guid actor, bool superAdministrator, int page, int pageSize, CancellationToken token)
    {
        if (!await HasScopeAsync(actor, superAdministrator, token).ConfigureAwait(false)) return Result<PagedResult<OrganizationReportingCandidate>>.Failure(ScopeDenied());
        if (employee == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Result<PagedResult<OrganizationReportingCandidate>>.Failure(Invalid());
        if (!(await members.FindActiveTenantMembersAsync([employee], token).ConfigureAwait(false)).ContainsKey(employee))
            return Result<PagedResult<OrganizationReportingCandidate>>.Failure(NotFound());
        var result = await members.ListActiveTenantMembersAsync(page, pageSize, token).ConfigureAwait(false);
        return Result<PagedResult<OrganizationReportingCandidate>>.Success(new(result.Items.Select(member => new OrganizationReportingCandidate(member.Id, member.Username, member.DisplayName)).ToArray(), result.Page, result.PageSize, result.Total));
    }

    public async Task<Result<OrganizationReportingLineResponse>> SetAsync(Guid employee, Guid actor, bool superAdministrator, SetOrganizationReportingLineRequest request, CancellationToken token)
    {
        if (!await HasScopeAsync(actor, superAdministrator, token).ConfigureAwait(false)) return Result<OrganizationReportingLineResponse>.Failure(ScopeDenied());
        if (employee == Guid.Empty || actor == Guid.Empty || request.ManagerUserId == Guid.Empty || request.ManagerUserId == employee || request.ExpectedRevision is < 0 or long.MaxValue)
            return Result<OrganizationReportingLineResponse>.Failure(Invalid());
        var requested = request.ManagerUserId is { } manager ? new[] { employee, manager } : [employee];
        var active = await members.FindActiveTenantMembersAsync(requested, token).ConfigureAwait(false);
        if (!active.ContainsKey(employee)) return Result<OrganizationReportingLineResponse>.Failure(NotFound());
        if (requested.Any(id => !active.ContainsKey(id))) return Result<OrganizationReportingLineResponse>.Failure(Invalid());
        var name = Name(active, request.ManagerUserId);
        return await transaction.ExecuteResultAsync(async inner =>
        {
            var guard = P(("GuardId", ids.NewId()));
            await command.ExecuteAsync(database.Value.Provider == DatabaseProvider.SqlServer ? OrganizationReportingLineSql.EnsureGuardSqlServer : OrganizationReportingLineSql.EnsureGuardMySql, guard, inner).ConfigureAwait(false);
            var locked = await query.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.SqlServer ? OrganizationReportingLineSql.LockGuardSqlServer : OrganizationReportingLineSql.LockGuardMySql, guard, inner).ConfigureAwait(false);
            if (locked == Guid.Empty) throw new InvalidOperationException("The reporting graph guard was not acquired.");
            var previous = await FindAsync(employee, inner).ConfigureAwait(false);
            if ((previous?.Revision ?? 0) != request.ExpectedRevision) return Result<OrganizationReportingLineResponse>.Failure(Conflict());
            if (previous?.ManagerUserId == request.ManagerUserId) return Result<OrganizationReportingLineResponse>.Success(Map(employee, previous, name));
            if (request.ManagerUserId is { } next && !await CanAttachAsync(employee, next, inner).ConfigureAwait(false))
                return Result<OrganizationReportingLineResponse>.Failure(new(OrganizationErrorCodes.ReportingLineCycle, "The reporting relationship would form a cycle or exceed the depth limit.", ErrorType.Validation));
            var now = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
            var id = previous?.Id ?? ids.NewId();
            var changed = await command.ExecuteAsync(previous is null ? OrganizationReportingLineSql.Insert : OrganizationReportingLineSql.Update,
                P(("Id", id), ("EmployeeUserId", employee), ("ManagerUserId", request.ManagerUserId), ("Revision", request.ExpectedRevision), ("ActorUserId", actor), ("Now", now)), inner).ConfigureAwait(false);
            if (changed != 1) return Result<OrganizationReportingLineResponse>.Failure(Conflict());
            return Result<OrganizationReportingLineResponse>.Success(new(employee, request.ManagerUserId, request.ExpectedRevision + 1, actor, now, name));
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> CanAttachAsync(Guid employee, Guid manager, CancellationToken token)
    {
        var seen = new HashSet<Guid> { employee }; var current = manager;
        for (var level = 0; level < MaximumReportingDepth; level++)
        {
            if (current == Guid.Empty || !seen.Add(current)) return false;
            var row = await FindAsync(current, token).ConfigureAwait(false);
            if (row is null) return true;
            if (row.Id == Guid.Empty || row.EmployeeUserId != current || row.Revision < 1) return false;
            if (row.ManagerUserId is null) return true;
            current = row.ManagerUserId.Value;
        }
        return false;
    }
    private Task<OrganizationReportingLineRecord?> FindAsync(Guid employee, CancellationToken token) => query.QuerySingleOrDefaultAsync<OrganizationReportingLineRecord>(OrganizationReportingLineSql.Find, P(("EmployeeUserId", employee)), token);
    private async Task<bool> HasScopeAsync(Guid actor, bool superAdministrator, CancellationToken token)
    {
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is null) throw new TenantContextMissingException("organization.tenant_context_required");
        return actor != Guid.Empty && (await scopes.ResolveAsync(actor, superAdministrator, token).ConfigureAwait(false)).IsUnrestricted;
    }
    private static string? Name(IReadOnlyDictionary<Guid, TenantUserDirectoryEntry> users, Guid? id) => id is { } value && users.TryGetValue(value, out var member) ? string.IsNullOrWhiteSpace(member.DisplayName) ? member.Username : member.DisplayName : null;
    private static OrganizationReportingLineResponse Map(Guid employee, OrganizationReportingLineRecord? row, string? name) => new(employee, row?.ManagerUserId, row?.Revision ?? 0, row?.UpdatedById, row?.UpdatedAtUtc, name);
    private static object P(params (string Name, object? Value)[] values) => OrganizationSqlParameters.Create(values);
    private static Error Invalid() => new(OrganizationErrorCodes.ReportingLineInvalid, "The reporting relationship or selected tenant member is invalid.", ErrorType.Validation);
    private static Error NotFound() => new(OrganizationErrorCodes.ReportingEmployeeNotFound, "The employee is not an active member of the current tenant.", ErrorType.NotFound);
    private static Error Conflict() => new(OrganizationErrorCodes.ReportingLineConflict, "The reporting relationship changed. Refresh before updating.", ErrorType.Conflict);
    private static Error ScopeDenied() => new(OrganizationErrorCodes.ReportingLineScopeDenied, "Full organization data scope is required to manage reporting relationships.", ErrorType.Forbidden);
}
