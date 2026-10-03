using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOrganizations;

/// <summary>仅所有者替换机构授予；Organization 校验在 AI 本地事务外，事务只修改 AI 表。</summary>
/// <param name="query">统一只读执行器。</param>
/// <param name="command">统一写入执行器。</param>
/// <param name="transaction">失败结果原子回滚的本模块事务。</param>
/// <param name="tenant">可信当前范围。</param>
/// <param name="organizations">当前范围权威活动机构目录。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="ids">应用端 UUID v7 生成器。</param>
internal sealed class AiKnowledgeOrganizationGrantService(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, ICurrentTenant tenant, ICurrentTenantOrganizationUnitDirectory organizations, IClock clock, IIdGenerator ids)
{
    private bool IsTenant => AiKnowledgeScope.IsTenant(tenant);
    internal async Task<Result<AiKnowledgeOrganizationGrantsResponse>> GetAsync(Guid knowledgeBaseId, Guid? documentId, Guid owner, CancellationToken token)
    {
        var version = await FindVersionAsync(knowledgeBaseId, documentId, owner, token).ConfigureAwait(false);
        if (version is null) return NotFound(documentId);
        var statement = documentId is null
            ? IsTenant ? AiKnowledgeOrganizationGrantSql.ListBaseTenant : AiKnowledgeOrganizationGrantSql.ListBaseHost
            : IsTenant ? AiKnowledgeOrganizationGrantSql.ListDocTenant : AiKnowledgeOrganizationGrantSql.ListDocHost;
        var grants = await query.QueryAsync<Guid>(statement, Parameters(knowledgeBaseId, documentId, owner), token).ConfigureAwait(false);
        return Result<AiKnowledgeOrganizationGrantsResponse>.Success(new(documentId ?? knowledgeBaseId, grants, version.Value));
    }
    internal async Task<Result<AiKnowledgeOrganizationGrantsResponse>> SetAsync(Guid knowledgeBaseId, Guid? documentId, Guid owner,
        SetAiKnowledgeOrganizationGrantsRequest request, CancellationToken token)
    {
        if (await FindVersionAsync(knowledgeBaseId, documentId, owner, token).ConfigureAwait(false) is null) return NotFound(documentId);
        if (request.Version < 1 || request.OrganizationUnitIds is null || request.OrganizationUnitIds.Count > 100 || request.OrganizationUnitIds.Contains(Guid.Empty)
            || request.OrganizationUnitIds.Distinct().Count() != request.OrganizationUnitIds.Count)
            return Failure(AiKnowledgeErrorCodes.OrganizationGrantsInvalid, "Organization grants require at most 100 unique nonempty organization unit identifiers and a positive version.", ErrorType.Validation);
        var grants = request.OrganizationUnitIds.Order().ToArray();
        // Host 没有租户机构范围；空名单仍可显式清除并递增共享版本。
        if (!IsTenant && grants.Length != 0) return Failure(AiKnowledgeErrorCodes.OrganizationUnavailable, "Host resources cannot grant tenant organizations.", ErrorType.BusinessRule);
        var active = IsTenant ? await organizations.FindActiveUnitsAsync(grants, token).ConfigureAwait(false) : new Dictionary<Guid, TenantOrganizationUnitDirectoryEntry>();
        if (active.Count != grants.Length) return Failure(AiKnowledgeErrorCodes.OrganizationUnavailable, "A requested organization unit is unavailable in the current scope.", ErrorType.BusinessRule);
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
                ? IsTenant ? AiKnowledgeOrganizationGrantSql.DeleteBaseTenant : AiKnowledgeOrganizationGrantSql.DeleteBaseHost
                : IsTenant ? AiKnowledgeOrganizationGrantSql.DeleteDocTenant : AiKnowledgeOrganizationGrantSql.DeleteDocHost;
            var insert = documentId is null
                ? IsTenant ? AiKnowledgeOrganizationGrantSql.InsertBaseTenant : AiKnowledgeOrganizationGrantSql.InsertBaseHost
                : IsTenant ? AiKnowledgeOrganizationGrantSql.InsertDocTenant : AiKnowledgeOrganizationGrantSql.InsertDocHost;
            await command.ExecuteAsync(delete, parameters, ct).ConfigureAwait(false);
            foreach (var unit in grants)
            {
                parameters["GrantId"] = ids.NewId();
                parameters["OrganizationUnitId"] = unit;
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
    private static Result<AiKnowledgeOrganizationGrantsResponse> NotFound(Guid? documentId) => Failure(documentId is null ? AiKnowledgeErrorCodes.NotFound : AiKnowledgeErrorCodes.DocumentNotFound, "The resource was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeOrganizationGrantsResponse> Failure(string code, string message, ErrorType type) => Result<AiKnowledgeOrganizationGrantsResponse>.Failure(new Error(code, message, type));
}
