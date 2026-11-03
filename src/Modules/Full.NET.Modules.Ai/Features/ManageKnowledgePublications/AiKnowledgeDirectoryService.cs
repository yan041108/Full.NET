using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgePublications;

/// <summary>目录维护不修改原文档授权或索引版本；批量条目各自提交与报告结果。</summary>
internal sealed class AiKnowledgeDirectoryService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    IBackgroundSessionAuthorization authorization, AiKnowledgeReadAccess access)
{
    private bool Scoped => AiKnowledgeScope.IsTenant(tenant);
    private SqlStatement Choose(SqlStatement host, SqlStatement scoped) => Scoped ? scoped : host;
    private async Task<bool> Authorized(SessionBindingSnapshot binding, bool write, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("Directory authorization cannot join a transaction.");
        if (binding.TenantId != tenant.Id || binding.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        string[] required = write ? [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgePublicationPermissions.DirectoryManage]
            : [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read];
        var actor = await authorization.AuthorizeAllAsync(binding, required, token).ConfigureAwait(false);
        return actor?.UserId == binding.UserId && actor.SessionId == binding.SessionId && actor.TenantId == binding.TenantId && binding.TenantId == tenant.Id;
    }
    private async Task<Dictionary<string, object?>> Parameters(Guid knowledge, SessionBindingSnapshot actor, CancellationToken token)
    {
        var p = await access.ParametersAsync(actor.UserId, token).ConfigureAwait(false);
        p["KnowledgeBaseId"] = knowledge; p["Id"] = knowledge; p["Now"] = clock.UtcNow;
        p["FolderId"] = null; p["ResponsibleUserId"] = null; p["Tag"] = null; p["State"] = null;
        return p;
    }
    private async Task<bool> VisibleBase(Dictionary<string, object?> p, CancellationToken token) =>
        await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false) is { IsEnabled: true };
    internal async Task<Result<IReadOnlyList<AiKnowledgeFolderResponse>>> FoldersAsync(Guid knowledge, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, false, token).ConfigureAwait(false)) return Denied<IReadOnlyList<AiKnowledgeFolderResponse>>();
        var p = await Parameters(knowledge, actor, token).ConfigureAwait(false);
        if (!await VisibleBase(p, token).ConfigureAwait(false)) return Missing<IReadOnlyList<AiKnowledgeFolderResponse>>();
        var rows = await Folders(p, token).ConfigureAwait(false);
        if (!await Authorized(actor, false, token).ConfigureAwait(false)) return Denied<IReadOnlyList<AiKnowledgeFolderResponse>>();
        p = await Parameters(knowledge, actor, token).ConfigureAwait(false);
        if (!await VisibleBase(p, token).ConfigureAwait(false)) return Missing<IReadOnlyList<AiKnowledgeFolderResponse>>();
        rows = await Folders(p, token).ConfigureAwait(false);
        return rows.Count <= AiKnowledgeDirectoryPolicy.MaxFolders ? Result<IReadOnlyList<AiKnowledgeFolderResponse>>.Success(rows) : Invalid<IReadOnlyList<AiKnowledgeFolderResponse>>();
    }
    private Task<IReadOnlyList<AiKnowledgeFolderResponse>> Folders(Dictionary<string, object?> p, CancellationToken token) =>
        query.QueryAsync<AiKnowledgeFolderResponse>(AiKnowledgeDirectorySql.FoldersFor(database.Value.Provider, Scoped), p, token);
    internal async Task<Result<AiKnowledgeFolderResponse>> SaveFolderAsync(Guid knowledge, Guid? id, SaveAiKnowledgeFolder request,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, true, token).ConfigureAwait(false)) return Denied<AiKnowledgeFolderResponse>();
        var name = AiKnowledgeDirectoryPolicy.Name(request.Name);
        if (name is null || request.ParentId == Guid.Empty || id == Guid.Empty || request.Version < 0
            || id.HasValue && request.Version < 1 || !id.HasValue && request.Version != 0) return Invalid<AiKnowledgeFolderResponse>();
        var p = await Parameters(knowledge, actor, token).ConfigureAwait(false);
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Missing<AiKnowledgeFolderResponse>();
            var rows = await Folders(p, ct).ConfigureAwait(false);
            var folderId = id ?? ids.NewId();
            if (rows.Count > AiKnowledgeDirectoryPolicy.MaxFolders || !ValidTree(rows, folderId, request.ParentId)
                || id.HasValue && !rows.Any(row => row.Id == id && row.Version == request.Version)) return Conflict<AiKnowledgeFolderResponse>();
            p["Id"] = folderId; p["ParentId"] = request.ParentId; p["Name"] = name; p["Version"] = request.Version;
            var sql = id.HasValue ? Choose(AiKnowledgeDirectorySql.UpdateFolderHost, AiKnowledgeDirectorySql.UpdateFolderTenant)
                : Choose(AiKnowledgeDirectorySql.InsertFolderHost, AiKnowledgeDirectorySql.InsertFolderTenant);
            return await command.ExecuteAsync(sql, p, ct).ConfigureAwait(false) == 1
                ? Result<AiKnowledgeFolderResponse>.Success(new(folderId, knowledge, request.ParentId, name, request.Version + 1)) : Conflict<AiKnowledgeFolderResponse>();
        }, token).ConfigureAwait(false);
    }
    private static bool ValidTree(IReadOnlyList<AiKnowledgeFolderResponse> folders, Guid id, Guid? parent)
    {
        var map = folders.ToDictionary(folder => folder.Id, folder => folder.ParentId); map[id] = parent;
        foreach (var start in map.Keys)
        {
            var seen = new HashSet<Guid>(); Guid? cursor = start;
            while (cursor.HasValue)
            { if (!seen.Add(cursor.Value) || seen.Count > 4 || !map.TryGetValue(cursor.Value, out cursor)) return false; }
        }
        return true;
    }
    internal async Task<Result<bool>> DeleteFolderAsync(Guid knowledge, Guid id, int version, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, true, token).ConfigureAwait(false)) return Denied<bool>();
        if (id == Guid.Empty || version < 1) return Invalid<bool>();
        var p = await Parameters(knowledge, actor, token).ConfigureAwait(false);
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Missing<bool>();
            var folders = await Folders(p, ct).ConfigureAwait(false);
            if (folders.Any(folder => folder.ParentId == id)) return Conflict<bool>();
            p["Id"] = id; p["Version"] = version;
            return await command.ExecuteAsync(Choose(AiKnowledgeDirectorySql.DeleteFolderHost, AiKnowledgeDirectorySql.DeleteFolderTenant), p, ct).ConfigureAwait(false) == 1
                ? Result<bool>.Success(true) : Conflict<bool>();
        }, token).ConfigureAwait(false);
    }
    internal async Task<Result<PagedResult<AiKnowledgeDirectoryResponse>>> ListAsync(Guid knowledge, Guid? folder, string? tag, Guid? responsible,
        string? state, int page, int pageSize, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, false, token).ConfigureAwait(false)) return Denied<PagedResult<AiKnowledgeDirectoryResponse>>();
        var offset = ((long)page - 1) * pageSize;
        var tags = tag is null ? [] : AiKnowledgeDirectoryPolicy.Tags([tag]);
        if (folder == Guid.Empty || responsible == Guid.Empty || tags is null || page < 1 || pageSize is < 1 or > 100 || offset > 100000
            || state is not (null or "draft" or "published" or "scheduled" or "expired")) return Invalid<PagedResult<AiKnowledgeDirectoryResponse>>();
        var p = await Parameters(knowledge, actor, token).ConfigureAwait(false);
        if (!await VisibleBase(p, token).ConfigureAwait(false)) return Missing<PagedResult<AiKnowledgeDirectoryResponse>>();
        p["FolderId"] = folder; p["Tag"] = tags.FirstOrDefault(); p["ResponsibleUserId"] = responsible;
        p["State"] = state; p["Offset"] = offset; p["PageSize"] = pageSize;
        var total = await query.QuerySingleOrDefaultAsync<long>(AiKnowledgeDirectorySql.CountFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false);
        var rows = await query.QueryAsync<AiKnowledgeDirectoryRecord>(AiKnowledgeDirectorySql.PageFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false);
        if (!await Authorized(actor, false, token).ConfigureAwait(false)) return Denied<PagedResult<AiKnowledgeDirectoryResponse>>();
        // 等待期间重新取得完整关系和最终投影，不把旧授权用于标题及发布状态。
        var fresh = await access.ParametersAsync(actor.UserId, token).ConfigureAwait(false);
        p["ActiveRoleIdsJson"] = fresh["ActiveRoleIdsJson"]; p["ActiveOrganizationUnitIdsJson"] = fresh["ActiveOrganizationUnitIdsJson"];
        rows = await query.QueryAsync<AiKnowledgeDirectoryRecord>(AiKnowledgeDirectorySql.PageFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false);
        total = await query.QuerySingleOrDefaultAsync<long>(AiKnowledgeDirectorySql.CountFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false);
        return Result<PagedResult<AiKnowledgeDirectoryResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, total));
    }
    internal async Task<Result<IReadOnlyList<AiKnowledgeDirectoryItemResult>>> BatchAsync(Guid knowledge, SetAiKnowledgeDirectoryBatch request,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (request.Items is not { Count: > 0 and <= 20 } || request.Items.Any(item => item is null)
            || request.Items.Select(item => item.DocumentId).Distinct().Count() != request.Items.Count) return Invalid<IReadOnlyList<AiKnowledgeDirectoryItemResult>>();
        var results = new List<AiKnowledgeDirectoryItemResult>();
        foreach (var item in request.Items)
        {
            token.ThrowIfCancellationRequested();
            var result = await SetItem(knowledge, item, actor, token).ConfigureAwait(false);
            results.Add(new(item.DocumentId, item.RequestKey, result.IsSuccess, result.IsSuccess ? result.Value : null, result.Error?.Code));
        }
        return Result<IReadOnlyList<AiKnowledgeDirectoryItemResult>>.Success(results);
    }
    private async Task<Result<int>> SetItem(Guid knowledge, SetAiKnowledgeDirectoryItem request, SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await Authorized(actor, true, token).ConfigureAwait(false)) return Denied<int>();
        var tags = AiKnowledgeDirectoryPolicy.Tags(request.Tags);
        if (tags is null || request.DocumentId == Guid.Empty || request.RequestKey == Guid.Empty || request.FolderId == Guid.Empty
            || request.ResponsibleUserId == Guid.Empty || request.Version < 0 || request.Version == int.MaxValue) return Invalid<int>();
        var canonical = request with { Tags = tags };
        var json = JsonSerializer.Serialize(canonical, AiJsonSerializerContext.Default.SetAiKnowledgeDirectoryItem);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var p = await Parameters(knowledge, actor, token).ConfigureAwait(false); p["DocumentId"] = request.DocumentId;
        return await transaction.ExecuteResultAsync(async ct =>
        {
            if (!await Lock(p, actor, ct).ConfigureAwait(false)) return Missing<int>();
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeDirectoryRecord>(AiKnowledgeDirectorySql.ItemFor(database.Value.Provider, Scoped), p, ct).ConfigureAwait(false);
            if (current is null) return Missing<int>();
            if (current.LastRequestKey == request.RequestKey) return current.RequestHash == hash ? Result<int>.Success(current.Version) : Conflict<int>();
            if (current.Version != request.Version) return Conflict<int>();
            if (request.FolderId.HasValue && !(await Folders(p, ct).ConfigureAwait(false)).Any(folder => folder.Id == request.FolderId)) return Invalid<int>();
            p["ResponsibleUserId"] = request.ResponsibleUserId;
            if (request.ResponsibleUserId.HasValue && await query.QuerySingleOrDefaultAsync<Guid>(Choose(AiKnowledgeDirectorySql.ResponsibleHost, AiKnowledgeDirectorySql.ResponsibleTenant), p, ct).ConfigureAwait(false) != knowledge) return Invalid<int>();
            p["FolderId"] = request.FolderId; p["TagsJson"] = JsonSerializer.Serialize(tags, AiJsonSerializerContext.Default.StringArray);
            p["RequestKey"] = request.RequestKey; p["RequestHash"] = hash; p["Version"] = request.Version;
            var statement = current.Version == 0 ? Choose(AiKnowledgeDirectorySql.InsertDirectoryHost, AiKnowledgeDirectorySql.InsertDirectoryTenant)
                : Choose(AiKnowledgeDirectorySql.UpdateDirectoryHost, AiKnowledgeDirectorySql.UpdateDirectoryTenant);
            return await command.ExecuteAsync(statement, p, ct).ConfigureAwait(false) == 1 ? Result<int>.Success(current.Version + 1) : Conflict<int>();
        }, token).ConfigureAwait(false);
    }
    private async Task<bool> Lock(Dictionary<string, object?> p, SessionBindingSnapshot actor, CancellationToken token)
    { p["Now"] = clock.UtcNow; return actor.TenantId == tenant.Id && await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeDirectorySql.LockFor(database.Value.Provider, Scoped), p, token).ConfigureAwait(false) == (Guid)p["KnowledgeBaseId"]! && actor.TenantId == tenant.Id; }
    private static AiKnowledgeDirectoryResponse Map(AiKnowledgeDirectoryRecord row) => new(row.Id, row.Title, row.FolderId,
        JsonSerializer.Deserialize(row.TagsJson, AiJsonSerializerContext.Default.StringArray) ?? [], row.ResponsibleUserId, row.Version,
        row.DocumentVersion, row.PublicationState, row.PublicationId, row.HeadVersion, row.CandidateVersionId, row.PendingPublicationId);
    private static Result<T> Error<T>(string code, ErrorType type) => Result<T>.Failure(new Error(code, "The knowledge directory operation is unavailable.", type));
    private static Result<T> Denied<T>() => Error<T>("authorization.permission_denied", ErrorType.Forbidden);
    private static Result<T> Missing<T>() => Error<T>("ai.knowledge.document_not_found", ErrorType.NotFound);
    private static Result<T> Invalid<T>() => Error<T>("ai.knowledge.directory_invalid", ErrorType.Validation);
    private static Result<T> Conflict<T>() => Error<T>("ai.knowledge.directory_conflict", ErrorType.Conflict);
}
