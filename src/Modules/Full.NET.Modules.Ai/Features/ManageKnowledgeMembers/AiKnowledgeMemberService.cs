using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeMembers;

/// <summary>所有者整量管理成员；跨模块活动用户核对在本地写事务之外完成。</summary>
/// <param name="query">统一查询执行器。</param>
/// <param name="command">统一命令执行器。</param>
/// <param name="transaction">失败结果回滚的本模块短事务。</param>
/// <param name="tenant">可信租户上下文。</param>
/// <param name="hostUsers">Host 活动用户批量目录。</param>
/// <param name="tenantMembers">当前租户活动成员目录。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="ids">应用端 UUID v7 生成器。</param>
internal sealed class AiKnowledgeMemberService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IHostUserBatchSelectionDirectory hostUsers,
    ITenantMemberSelectionDirectory tenantMembers, IClock clock, IIdGenerator ids)
{
    internal async Task<Result<AiKnowledgeMembersResponse>> GetAsync(Guid id, Guid owner, CancellationToken token)
    {
        var current = await FindOwnedAsync(id, owner, token).ConfigureAwait(false);
        if (current is null) return NotFound();
        var members = await query.QueryAsync<Guid>(AiKnowledgeScope.IsTenant(tenant)
                ? AiKnowledgeMemberSql.ListTenant : AiKnowledgeMemberSql.ListHost,
            AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner)), token).ConfigureAwait(false);
        return Result<AiKnowledgeMembersResponse>.Success(new(id, members.Order().ToArray(), current.Version));
    }

    internal async Task<Result<AiKnowledgeMembersResponse>> SetAsync(Guid id, Guid owner,
        SetAiKnowledgeMembersRequest request, CancellationToken token)
    {
        if (await FindOwnedAsync(id, owner, token).ConfigureAwait(false) is null) return NotFound();
        if (AiKnowledgeMembers.Validate(owner, request.UserIds, request.Version) is { } error)
            return Failure(AiKnowledgeErrorCodes.MembersInvalid, error, ErrorType.Validation);
        var members = request.UserIds.Order().ToArray();
        var isTenant = AiKnowledgeScope.IsTenant(tenant);
        // 单次替换最多 100 个用户；Tenant 目录每次按可信上下文核对，不能借 Host 目录扩大范围。
        if (isTenant)
        {
            foreach (var member in members)
                if (await tenantMembers.FindActiveTenantMemberAsync(member, token).ConfigureAwait(false) is null)
                    return Unavailable();
        }
        else if (members.Length > 0)
        {
            var available = await hostUsers.FindActiveHostUsersAsync(members, token).ConfigureAwait(false);
            if (available.Count != members.Length) return Unavailable();
        }

        return await transaction.ExecuteResultAsync(async ct =>
        {
            var parameters = AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner), ("Version", request.Version), ("UpdatedAtUtc", clock.UtcNow));
            var changed = await command.ExecuteAsync(isTenant ? AiKnowledgeMemberSql.BumpTenant : AiKnowledgeMemberSql.BumpHost,
                parameters, ct).ConfigureAwait(false);
            if (changed != 1)
                return await FindOwnedAsync(id, owner, ct).ConfigureAwait(false) is null ? NotFound()
                    : Failure(AiKnowledgeErrorCodes.VersionConflict, "The knowledge base was modified by another request.", ErrorType.Conflict);
            await command.ExecuteAsync(isTenant ? AiKnowledgeMemberSql.DeleteTenant : AiKnowledgeMemberSql.DeleteHost,
                parameters, ct).ConfigureAwait(false);
            foreach (var member in members)
                await command.ExecuteAsync(isTenant ? AiKnowledgeMemberSql.InsertTenant : AiKnowledgeMemberSql.InsertHost,
                    AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner), ("MemberId", ids.NewId()),
                        ("UserId", member), ("CreatedAtUtc", clock.UtcNow)), ct).ConfigureAwait(false);
            return await GetAsync(id, owner, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private Task<AiKnowledgeBaseRecord?> FindOwnedAsync(Guid id, Guid owner, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeScope.IsTenant(tenant)
                ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost,
            AiSqlParameters.Create(("Id", id), ("OwnerUserId", owner)), token);
    private static Result<AiKnowledgeMembersResponse> Failure(string code, string message, ErrorType type) =>
        Result<AiKnowledgeMembersResponse>.Failure(new Error(code, message, type));
    private static Result<AiKnowledgeMembersResponse> NotFound() => Failure(AiKnowledgeErrorCodes.NotFound, "The knowledge base was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeMembersResponse> Unavailable() => Failure(AiKnowledgeErrorCodes.MemberUnavailable, "The member is unavailable in the current scope.", ErrorType.BusinessRule);
}
