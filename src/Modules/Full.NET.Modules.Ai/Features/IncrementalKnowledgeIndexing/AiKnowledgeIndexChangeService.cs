using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>有界只读来源目录；最后一次授权等待之后再次确认知识库所有者与版本。</summary>
internal sealed class AiKnowledgeIndexChangeService(IQueryExecutor query, IDataTransactionState transactions,
    ICurrentTenant tenant, AiKnowledgePermissionGuard permissions, IOptions<DatabaseOptions> database)
{
    internal static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexChangesRead];

    internal async Task<Result<AiKnowledgeIndexChangePage>> ListAsync(Guid knowledgeBaseId, Guid actor,
        Guid? afterDocumentId, int pageSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge directory cannot join an ambient transaction.");
        if (pageSize is < 1 or > 50 || afterDocumentId == Guid.Empty)
            return Fail(AiKnowledgeErrorCodes.InputInvalid, "Directory page size must be 1..50 and the cursor must be nonempty.", ErrorType.Validation);
        var tenantId = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        if (actor == Guid.Empty || tenantId == Guid.Empty || scoped != tenantId.HasValue) return Denied();
        if (knowledgeBaseId == Guid.Empty) return Missing();
        var p = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = knowledgeBaseId, ["OwnerUserId"] = actor, ["Take"] = pageSize + 1,
            ["HasCursor"] = afterDocumentId.HasValue ? 1 : 0, ["AfterDocumentId"] = afterDocumentId ?? knowledgeBaseId
        };
        var ownerStatement = scoped ? AiKnowledgeIndexChangeSql.OwnerTenant : AiKnowledgeIndexChangeSql.OwnerHost;
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        var ownerVersion = await query.QuerySingleOrDefaultAsync<int>(ownerStatement, p, token).ConfigureAwait(false);
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        if (ownerVersion <= 0) return Missing();
        p["KnowledgeBaseVersion"] = ownerVersion;
        var rows = await query.QueryAsync<AiKnowledgeIndexChangeItem>(AiKnowledgeIndexChangeSql.PageFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var count = rows.Count;
        if (count > pageSize + 1) throw new InvalidOperationException("Knowledge directory exceeded its bound.");
        // 在后续授权等待前复制有界集合，不能把可变驱动集合交给公开响应。
        var snapshot = new AiKnowledgeIndexChangeItem[count];
        for (var index = 0; index < count; index++) snapshot[index] = rows[index];
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        if (snapshot.Any(row => row is null || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty || row.VersionNumber <= 0
            || string.IsNullOrWhiteSpace(row.Title) || row.Title.Length > 200)
            || snapshot.Select(row => row.DocumentId).Distinct().Count() != count)
            throw new InvalidOperationException("Knowledge directory returned invalid source identities.");
        // 上面的会话权限复核是本页准入截点；后续只做 AI 权威复核，不无限交替跨模块授权与数据读取。
        var currentVersion = await query.QuerySingleOrDefaultAsync<int>(ownerStatement, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!SameScope()) return Denied();
        if (currentVersion <= 0) return Missing();
        if (currentVersion != ownerVersion) return Fail(AiKnowledgeErrorCodes.VersionConflict, "The knowledge directory changed; refresh before continuing.", ErrorType.Conflict);
        // 授权等待期间文档可更新、删除或确认新来源；固定再读一页，不能按旧来源打开后续操作。
        var finalRows = await query.QueryAsync<AiKnowledgeIndexChangeItem>(AiKnowledgeIndexChangeSql.PageFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!SameScope()) return Denied();
        if (finalRows.Count != count)
            return Fail(AiKnowledgeErrorCodes.VersionConflict, "The knowledge directory changed; refresh before continuing.", ErrorType.Conflict);
        for (var index = 0; index < count; index++)
            if (snapshot[index] != finalRows[index])
                return Fail(AiKnowledgeErrorCodes.VersionConflict, "The knowledge directory changed; refresh before continuing.", ErrorType.Conflict);
        var take = Math.Min(count, pageSize);
        return Result<AiKnowledgeIndexChangePage>.Success(new(snapshot.Take(take).ToArray(), count > take ? snapshot[take - 1].DocumentId : null));

        bool SameScope() => tenant.Id == tenantId && AiKnowledgeScope.IsTenant(tenant) == scoped;
        async Task<bool> Authorized()
        {
            token.ThrowIfCancellationRequested();
            if (!SameScope()) return false;
            var allowed = await permissions.IsAuthorizedAsync(Permissions, actor, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return allowed && SameScope();
        }
    }

    private static Result<AiKnowledgeIndexChangePage> Denied() => Fail("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIndexChangePage> Missing() => Fail(AiKnowledgeErrorCodes.NotFound, "The enabled knowledge base was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeIndexChangePage> Fail(string code, string message, ErrorType type) => Result<AiKnowledgeIndexChangePage>.Failure(new Error(code, message, type));
}
