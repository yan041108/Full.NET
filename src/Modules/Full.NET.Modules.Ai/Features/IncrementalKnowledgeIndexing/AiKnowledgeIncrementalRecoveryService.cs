using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>所有者显式分页补齐历史与停用期间意图；逐文档授权准入，不追溯撤销已经开始的事务。</summary>
/// <remarks>当前一条意图事务可能在撤权后提交；下一条前再次授权，且拒绝响应不暴露计数，不创建收费工作。</remarks>
internal sealed class AiKnowledgeIncrementalRecoveryService(IQueryExecutor query, IDataTransactionState transactions,
    AiKnowledgeIncrementalIndexWriter writer, ICurrentTenant tenant, AiKnowledgePermissionGuard permissions, IOptions<DatabaseOptions> database)
{
    internal static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRecover];

    internal async Task<Result<AiKnowledgeIncrementalRecoveryResponse>> RecoverAsync(Guid knowledgeBaseId, Guid actor,
        AiKnowledgeIncrementalRecoveryRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Knowledge recovery cannot join an ambient transaction.");
        if (request.PageSize is < 1 or > 50 || request.AfterDocumentId == Guid.Empty)
            return Fail(AiKnowledgeErrorCodes.InputInvalid, "Recovery page size must be 1..50 and the cursor must be a nonempty document id.", ErrorType.Validation);
        var tenantId = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        if (actor == Guid.Empty || tenantId == Guid.Empty || scoped != tenantId.HasValue) return Denied();
        if (knowledgeBaseId == Guid.Empty) return Missing();
        var p = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = knowledgeBaseId, ["OwnerUserId"] = actor, ["Take"] = request.PageSize + 1,
            ["HasCursor"] = request.AfterDocumentId.HasValue ? 1 : 0,
            // 不使用空 UUID 占位，首次扫描由 HasCursor 显式关闭游标条件。
            ["AfterDocumentId"] = request.AfterDocumentId ?? knowledgeBaseId
        };
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        var owned = await query.QuerySingleOrDefaultAsync<int>(scoped ? AiKnowledgeIncrementalRecoverySql.OwnerTenant : AiKnowledgeIncrementalRecoverySql.OwnerHost, p, token).ConfigureAwait(false);
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        if (owned != 1) return Missing();
        var rows = await query.QueryAsync<AiKnowledgeSourceConfirmed>(AiKnowledgeIncrementalRecoverySql.PageFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (!await Authorized().ConfigureAwait(false)) return Denied();
        if (rows.Count > request.PageSize + 1 || rows.Any(r => r.KnowledgeBaseId != knowledgeBaseId || r.TenantId != tenantId || r.DocumentId == Guid.Empty || r.VersionId == Guid.Empty)
            || rows.Select(r => r.DocumentId).Distinct().Count() != rows.Count)
            throw new InvalidOperationException("Knowledge recovery returned invalid source identities.");
        var count = Math.Min(rows.Count, request.PageSize); var changed = 0;
        for (var index = 0; index < count; index++)
        {
            if (!await Authorized().ConfigureAwait(false)) return Denied();
            if (await writer.ApplyAsync(rows[index], token).ConfigureAwait(false)) changed++;
            if (!await Authorized().ConfigureAwait(false)) return Denied();
        }
        return Result<AiKnowledgeIncrementalRecoveryResponse>.Success(new(count, changed, rows.Count > count ? rows[count - 1].DocumentId : null));

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

    private static Result<AiKnowledgeIncrementalRecoveryResponse> Denied() => Fail("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden);
    private static Result<AiKnowledgeIncrementalRecoveryResponse> Missing() => Fail(AiKnowledgeErrorCodes.NotFound, "The enabled knowledge base was not found.", ErrorType.NotFound);
    private static Result<AiKnowledgeIncrementalRecoveryResponse> Fail(string code, string message, ErrorType type) => Result<AiKnowledgeIncrementalRecoveryResponse>.Failure(new Error(code, message, type));
}
