using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeRoles;

/// <summary>仅所有者替换角色授予；Identity 校验在 AI 本地事务外，事务只修改 AI 表。</summary>
/// <param name="query">统一只读执行器。</param>
/// <param name="command">统一写入执行器。</param>
/// <param name="transaction">失败结果原子回滚的本模块事务。</param>
/// <param name="tenant">可信当前范围。</param>
/// <param name="roles">当前范围权威活动角色目录。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="ids">应用端 UUID v7 生成器。</param>
internal sealed class AiKnowledgeRoleGrantService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, IActiveRoleDirectory roles, IClock clock, IIdGenerator ids)
{
    private bool IsTenant => AiKnowledgeScope.IsTenant(tenant);
    internal async Task<Result<AiKnowledgeRoleGrantsResponse>> GetAsync(Guid knowledgeBaseId, Guid? documentId, Guid owner, CancellationToken token)
    {
        var version = await FindVersionAsync(knowledgeBaseId, documentId, owner, token).ConfigureAwait(false);
        if (version is null) return NotFound(documentId);
        var statement = documentId is null
            ? IsTenant ? AiKnowledgeRoleGrantSql.ListBaseTenant : AiKnowledgeRoleGrantSql.ListBaseHost
            : IsTenant ? AiKnowledgeRoleGrantSql.ListDocTenant : AiKnowledgeRoleGrantSql.ListDocHost;
        var grants = await query.QueryAsync<Guid>(statement, Parameters(knowledgeBaseId, documentId, owner), token).ConfigureAwait(false);
        return Result<AiKnowledgeRoleGrantsResponse>.Success(new(documentId ?? knowledgeBaseId, grants, version.Value));
    }
    internal async Task<Result<AiKnowledgeRoleGrantsResponse>> SetAsync(Guid knowledgeBaseId, Guid? documentId, Guid owner,
        SetAiKnowledgeRoleGrantsRequest request, CancellationToken token)
    {
        if (await FindVersionAsync(knowledgeBaseId, documentId, owner, token).ConfigureAwait(false) is null) return NotFound(documentId);
        if (request.Version < 1 || request.RoleIds is null || request.RoleIds.Count > 100 || request.RoleIds.Contains(Guid.Empty)
            || request.RoleIds.Distinct().Count() != request.RoleIds.Count)
            return Failure(AiKnowledgeErrorCodes.RoleGrantsInvalid, "Role grants require at most 100 unique nonempty role identifiers and a positive version.", ErrorType.Validation);
        var grants = request.RoleIds.Order().ToArray();
        var active = await roles.FindActiveRolesAsync(grants, token).ConfigureAwait(false);
        if (active.Count != grants.Length) return Failure(AiKnowledgeErrorCodes.RoleUnavailable, "A requested role is unavailable in the current scope.", ErrorType.BusinessRule);
        return await transaction.ExecuteResultAsync(async ct =>
        {
            var parameters = Parameters(knowledgeBaseId, documentId, owner);
            parameters.Add("Id", knowledgeBaseId);
            parameters.Add("Version", request.Version);
            parameters.Add("UpdatedAtUtc", clock.UtcNow);
            var bump = documentId is null
                ? IsTenant ? AiKnowledgeMemberSql.BumpTenant : AiKnowledgeMemberSql.BumpHost
                : IsTenant ? AiKnowledgeDocumentSql.BumpTenant : AiKnowledgeDocumentSql.BumpHost;
            if (await command.ExecuteAsync(bump, parameters, ct).ConfigureAwait(false) != 1)
                return await FindVersionAsync(knowledgeBaseId, documentId, owner, ct).ConfigureAwait(false) is null ? NotFound(documentId)
                    : Failure(documentId is null ? AiKnowledgeErrorCodes.VersionConflict : AiKnowledgeErrorCodes.DocumentVersionConflict,
                        "The resource was modified by another request.", ErrorType.Conflict);
            var delete = documentId is null
                ? IsTenant ? AiKnowledgeRoleGrantSql.DeleteBaseTenant : AiKnowledgeRoleGrantSql.DeleteBaseHost
                : IsTenant ? AiKnowledgeRoleGrantSql.DeleteDocTenant : AiKnowledgeRoleGrantSql.DeleteDocHost;
            var insert = documentId is null
                ? IsTenant ? AiKnowledgeRoleGrantSql.InsertBaseTenant : AiKnowledgeRoleGrantSql.InsertBaseHost
                : IsTenant ? AiKnowledgeRoleGrantSql.InsertDocTenant : AiKnowledgeRoleGrantSql.InsertDocHost;
            await command.ExecuteAsync(delete, parameters, ct).ConfigureAwait(false);
            foreach (var role in grants)
            {
                parameters["GrantId"] = ids.NewId();
                parameters["RoleId"] = role;
                parameters["CreatedAtUtc"] = clock.UtcNow;
                if (await command.ExecuteAsync(insert, parameters, ct).ConfigureAwait(false) != 1) return NotFound(documentId);
            }
            // 只回读 AI 自有授予和版本，不在本地事务中重新调用目录 Port。
            return await GetAsync(knowledgeBaseId, documentId, owner, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }
    private async Task<int?> FindVersionAsync(Guid knowledgeBaseId, Guid? documentId, Guid owner, CancellationToken token)
    {
        if (knowledgeBaseId == Guid.Empty || documentId == Guid.Empty) return null;
        var parameters = Parameters(knowledgeBaseId, documentId, owner);
        if (documentId is not null)
            return (await query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(IsTenant ? AiKnowledgeDocumentSql.FindTenant : AiKnowledgeDocumentSql.FindHost, parameters, token).ConfigureAwait(false))?.Version;
        parameters.Add("Id", knowledgeBaseId);
        return (await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(IsTenant ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost, parameters, token).ConfigureAwait(false))?.Version;
    }
    private static Dictionary<string, object?> Parameters(Guid knowledgeBaseId, Guid? documentId, Guid owner)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["KnowledgeBaseId"] = knowledgeBaseId, ["OwnerUserId"] = owner };
        if (documentId is { } id) parameters.Add("DocumentId", id);
        return parameters;
    }
    private static Result<AiKnowledgeRoleGrantsResponse> NotFound(Guid? documentId) => Failure(documentId is null ? AiKnowledgeErrorCodes.NotFound : AiKnowledgeErrorCodes.DocumentNotFound, "The resource was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeRoleGrantsResponse> Failure(string code, string message, ErrorType type) => Result<AiKnowledgeRoleGrantsResponse>.Failure(new Error(code, message, type));
}
