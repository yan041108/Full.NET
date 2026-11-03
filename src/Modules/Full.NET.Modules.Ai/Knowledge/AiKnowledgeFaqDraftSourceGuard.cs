using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>列表、正文、历史和修改共同核对完整来源；不借用聊天权限或把客户端摘要当成完成证明。</summary>
internal sealed class AiKnowledgeFaqDraftSourceGuard(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access, IAiKnowledgeAnswerSourceProof proof,
    IBackgroundSessionAuthorization? background = null, AiKnowledgePermissionGuard? permissions = null, AiKnowledgeFaqNodeReader? nodes = null, AiKnowledgeFaqClassification? classification = null,
    AiPersonalScheduleSourceReader? personalSchedules = null)
{
    internal Task<IReadOnlySet<Guid>> AuthorizeAsync(IReadOnlyList<AiKnowledgeFaqDraftRecord> records, Guid actor, string operation, CancellationToken token, bool requireStored = true, bool requireOwner = true, SessionBindingSnapshot? original = null, AiKnowledgeBaseRecord? exportRoot = null) =>
        AuthorizeCoreAsync(records, actor, operation, token, requireStored, requireOwner, original, verifyPayloads: true, exportRoot);

    /// <summary>仅供索引阶段复核完整绑定和当前权限；不交付正文，也不能替代正文读取、收费派发和响应的载荷核证。</summary>
    internal Task<IReadOnlySet<Guid>> AuthorizeIndexBindingsAsync(AiKnowledgeFaqDraftRecord record, SessionBindingSnapshot original, CancellationToken token) =>
        AuthorizeCoreAsync([record], original.UserId, AiKnowledgeFaqPermissions.Index, token, true, true, original, verifyPayloads: false);

    /// <summary>检索前后仅核证来源身份和当前权限；不能交付正文或替代检索完整载荷证明。</summary>
    internal Task<IReadOnlySet<Guid>> AuthorizeReadBindingsAsync(AiKnowledgeFaqDraftRecord record, Guid actor, CancellationToken token) =>
        AuthorizeCoreAsync([record], actor, AiKnowledgeFaqPermissions.Read, token, true, false, null, verifyPayloads: false);

    private async Task<IReadOnlySet<Guid>> AuthorizeCoreAsync(IReadOnlyList<AiKnowledgeFaqDraftRecord> records, Guid actor, string operation, CancellationToken token,
        bool requireStored, bool requireOwner, SessionBindingSnapshot? original, bool verifyPayloads, AiKnowledgeBaseRecord? exportRoot = null)
    {
        token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("FAQ 来源核证不能加入事务。");
        if (records.Count > 26) throw new InvalidDataException(); var tenantId = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        var valid = new Dictionary<Guid, AiKnowledgeFaqProvenance>();
        foreach (var row in records)
            try { if (row.TenantId == tenantId) valid.Add(row.Id, AiKnowledgeFaqProvenanceCodec.Decode(row)); } catch (InvalidDataException) { }
        // 绑定路径不交付正文，业务权威在最后联合 SQL 等待后核证；正文路径在读取前后各核证一次。
        var bindingOnly = !verifyPayloads && requireStored;
        if (!bindingOnly) await VerifyBusinessAsync().ConfigureAwait(false);
        if (valid.Values.Any(value => value.RequiresFeedbackPermission)
            && !await AuthorizedAsync([AiKnowledgeFaqPermissions.Feedback], actor, original, token).ConfigureAwait(false))
            foreach (var id in valid.Where(item => item.Value.RequiresFeedbackPermission).Select(item => item.Key).ToArray()) valid.Remove(id);
        var dependencies = valid.Values.SelectMany(value => value.Dependencies).Distinct().ToArray();
        if (dependencies.Length > 208) throw new InvalidDataException();
        var sourceJson = JsonSerializer.Serialize(dependencies, AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
        var proven = new HashSet<AiKnowledgeAnswerDependency>();
        var capturedParents = new Dictionary<Guid, IReadOnlyList<AiKnowledgeFaqParentAuthorization>>();
        for (var pass = 0; pass < (bindingOnly ? 1 : 2); pass++)
        {
            Check();
            // 元数据路径没有两轮载荷 IO；入口一次完整权限核对、出口一次完整权限核对与联合 SQL 即可。
            // 正文/收费路径保留原两轮实际载荷和撤权核证，不能从该路径取得正文授权。
            string[] initial = bindingOnly ? RequiredPermissions() : [AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, operation];
            if (!bindingOnly && valid.Values.Any(value => value.RequiresFeedbackPermission)) initial = [.. initial, AiKnowledgeFaqPermissions.Feedback];
            if (!await AuthorizedAsync(initial, actor, original, token).ConfigureAwait(false)) return new HashSet<Guid>();
            if (pass == 0)
                foreach (var candidate in valid.ToArray())
                {
                    var captured = await ParentBindingsCoreAsync(candidate.Value, actor, token, pass == 0 && verifyPayloads, original, verifyBusiness: false).ConfigureAwait(false);
                    if (captured is null) valid.Remove(candidate.Key); else capturedParents[candidate.Key] = captured;
                }
            Check();
            // 阶段绑定不交付正文；最后 ReadableLeaves SQL 已逐项绑定当前发布、完整代次及读者范围，避免每层再次搬运完整行。
            // 正文/收费门禁仍执行下面的两轮实际来源与载荷证明；未落库候选也不能省略其叶核证。
            if (!verifyPayloads && requireStored || dependencies.Length == 0) continue;
            if (!await AuthorizedAsync([AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead], actor, original, token).ConfigureAwait(false)) return new HashSet<Guid>();
            var parameters = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check(); parameters["DependenciesJson"] = sourceJson;
            // 文档叶协议禁止夹带受管 FAQ；父 FAQ 只能由独立图节点保留修订及当前发布资格。
            if (await query.QuerySingleOrDefaultAsync<int>(AiKnowledgeFaqDraftSql.BoundDocumentsFor(database.Value.Provider, scoped), parameters, token).ConfigureAwait(false) != 0) return new HashSet<Guid>();
            var rows = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), parameters, token).ConfigureAwait(false); Check();
            if (rows.Select(row => (row.DocumentId, row.VersionId)).Distinct().Count() != rows.Count) return new HashSet<Guid>();
            foreach (var candidate in valid.ToArray())
                if (candidate.Value.Dependencies.Any(dependency => !rows.Any(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row)) || pass == 1 && !proven.Contains(dependency))) valid.Remove(candidate.Key);
            if (pass == 0)
                foreach (var dependency in valid.Values.SelectMany(value => value.Dependencies).Distinct())
                {
                    token.ThrowIfCancellationRequested(); var row = rows.Single(row => AiKnowledgeAnswerLineageCodec.Matches(dependency, row));
                    var sourceParameters = new Dictionary<string, object?>(parameters) { ["KnowledgeBaseId"] = row.KnowledgeBaseId, ["DocumentId"] = row.DocumentId, ["VersionId"] = row.VersionId };
                    if (!verifyPayloads || await proof.VerifyAsync(row, sourceParameters, token).ConfigureAwait(false)) proven.Add(dependency); Check();
                }
        }
        Check();
        if (valid.Count == 0) return new HashSet<Guid>();
        if (!requireStored && valid.Values.All(value => value.Graph is null && value.Feedback is null))
        {
            await VerifyBusinessAsync().ConfigureAwait(false);
            return valid.Keys.ToHashSet();
        }
        if (records.Select(row => row.KnowledgeBaseId).Distinct().Count() != 1) throw new InvalidDataException();
        var required = RequiredPermissions();
        if (!bindingOnly && !await AuthorizedAsync(required, actor, original, token).ConfigureAwait(false)) return new HashSet<Guid>();
        var final = await access.ParametersAsync(actor, token).ConfigureAwait(false); Check();
        if (!await AuthorizedAsync(required, actor, original, token).ConfigureAwait(false)) return new HashSet<Guid>();
        Check();
        final["DependenciesJson"] = sourceJson; final["KnowledgeBaseId"] = records[0].KnowledgeBaseId;
        final["FaqFeedbackReadAllowed"] = valid.Values.Any(value => value.RequiresFeedbackPermission) ? 1 : 0;
        // 父载荷与叶一样在本次门禁证明一次；捕获值不授予后续阶段权限。
        // 权限/机构刚刚重读，最后单条 SQL 再联合比较全部父发布、模型目的地及库版本，等待后变化则整次拒绝。
        var parents = valid.Keys.SelectMany(id => capturedParents[id]).ToArray();
        final["FaqParentBindingsJson"] = JsonSerializer.Serialize(parents, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
        final["FaqBindingsJson"] = JsonSerializer.Serialize(records.Where(row => valid.ContainsKey(row.Id)).Select(row => new AiKnowledgeFaqDraftBinding(row.Id, row.SourceHash, row.HeadRevisionId, row.HeadVersion)).ToArray(), AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqDraftBindingArray);
        // 未落库候选没有根可查，也必须以同一父集合谓词结束核证，不能把元数据加载结果当成发布资格。
        if (!requireStored)
        {
            var pendingStatement = AiKnowledgeFaqParentSql.For(database.Value.Provider, scoped);
            if (valid.Values.Any(value => value.RequiresFeedbackPermission))
            {
                if (records.Count != 1) throw new InvalidDataException();
                final["Id"] = records[0].KnowledgeBaseId;
                var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), final, token).ConfigureAwait(false);
                Check(); if (root is not { IsEnabled: true, DataClassification: "restricted" } || root.OwnerUserId != actor) return new HashSet<Guid>();
                final["SourceJson"] = records[0].SourceJson; final["OriginKey"] = records[0].OriginKey; final["OwnerUserId"] = actor;
                final["TargetKnowledgeVersion"] = root.Version; final["TargetClassification"] = root.DataClassification;
                pendingStatement = AiKnowledgeFaqFeedbackSql.PendingGraphFor(database.Value.Provider, scoped);
            }
            var qualified = await query.QuerySingleOrDefaultAsync<int>(pendingStatement, final, token).ConfigureAwait(false);
            Check(); await VerifyBusinessAsync().ConfigureAwait(false); return qualified == 1 ? valid.Keys.ToHashSet() : new HashSet<Guid>();
        }
        if (exportRoot is not null)
        {
            if (operation != AiKnowledgeFaqPermissions.Export || !requireOwner || exportRoot.Id != records[0].KnowledgeBaseId) throw new InvalidDataException();
            final["TransferKnowledgeVersion"] = exportRoot.Version; final["TransferClassification"] = exportRoot.DataClassification;
        }
        var statement = exportRoot is not null ? AiKnowledgeFaqDraftSql.ExportCurrentFor(database.Value.Provider, scoped)
            : requireOwner ? AiKnowledgeFaqDraftSql.CurrentFor(database.Value.Provider, scoped) : AiKnowledgeFaqDraftSql.SharedCurrentFor(database.Value.Provider, scoped);
        var current = await query.QueryAsync<Guid>(statement, final, token).ConfigureAwait(false); Check();
        foreach (var id in valid.Keys.Where(id => !current.Contains(id)).ToArray()) valid.Remove(id);
        // Current/SharedCurrent/ExportCurrent 已在同一 SQL 核对根 ACL、启用及业务来源 restricted 下限，不能再逐条重查相同根。
        await VerifyBusinessAsync(rootAlreadyBound: true).ConfigureAwait(false);
        return current.Where(valid.ContainsKey).ToHashSet();

        async Task VerifyBusinessAsync(bool rootAlreadyBound = false)
        {
            foreach (var candidate in valid.Where(value => value.Value.PersonalSchedules is not null).ToArray())
            {
                Check();
                // 每个原始来源单独失败关闭，不让一条私有来源拒绝影响同页独立条目的可读性。
                var proofs = candidate.Value.PersonalSchedules!;
                if (proofs.Any(value => !AiPersonalScheduleAnswerSources.Valid(value, actor, tenantId)) || personalSchedules is null) valid.Remove(candidate.Key);
                else
                {
                    var rootAllowed = rootAlreadyBound;
                    if (!rootAllowed)
                    {
                        var row = records.Single(value => value.Id == candidate.Key);
                        var parameters = await access.ParametersAsync(actor, token).ConfigureAwait(false); parameters["Id"] = row.KnowledgeBaseId;
                        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), parameters, token).ConfigureAwait(false);
                        rootAllowed = root is { IsEnabled: true, DataClassification: "restricted" };
                    }
                    // 本地根与 ACL 等待结束后再调用所有者权威，避免根查询等待掩盖刚发生的日程撤权或版本改变。
                    if (!rootAllowed
                        || !await personalSchedules.RevalidateAsync(actor, proofs, token, original).ConfigureAwait(false)) valid.Remove(candidate.Key);
                }
                Check();
            }
        }

        string[] RequiredPermissions()
        {
            string[] result = dependencies.Length == 0 && valid.Values.All(value => value.Graph is null)
                ? [AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, operation]
                : [AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, operation, AiKnowledgeDocumentPermissions.Read,
                    AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead];
            return valid.Values.Any(value => value.RequiresFeedbackPermission) ? [.. result, AiKnowledgeFaqPermissions.Feedback] : result;
        }
        void Check() { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction || tenant.Id != tenantId || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException(); }
    }
    /// <summary>父节点逐个核证当前服务资格并严格比对完整祖先图；最终 SQL 联合绑定全部当前模型目的地。</summary>
    internal Task<IReadOnlyList<AiKnowledgeFaqParentAuthorization>?> ParentBindingsAsync(AiKnowledgeFaqProvenance origin, Guid actor,
        CancellationToken token, bool verifyPayloads, SessionBindingSnapshot? original = null) =>
        ParentBindingsCoreAsync(origin, actor, token, verifyPayloads, original, verifyBusiness: true);

    private async Task<IReadOnlyList<AiKnowledgeFaqParentAuthorization>?> ParentBindingsCoreAsync(AiKnowledgeFaqProvenance origin, Guid actor,
        CancellationToken token, bool verifyPayloads, SessionBindingSnapshot? original, bool verifyBusiness)
    {
        // 完整来源门禁已在本次调用的入口/出口覆盖合并业务依赖；独立调用仍自行核证，不跨阶段缓存授权。
        if (verifyBusiness && !await BusinessAsync(origin, actor, token, original).ConfigureAwait(false)) return null;
        if (origin.Graph is null) return [];
        if (nodes is null || classification is null) return null;
        AiKnowledgeFaqGraphs.Validate(origin.Graph, origin.Dependencies);
        string[] required = origin.Graph.Nodes.Any(node => node.Source.FeedbackDerived)
            ? [.. AiKnowledgeFaqLifecycleAccess.ReadPermissions, AiKnowledgeFaqPermissions.Feedback] : AiKnowledgeFaqLifecycleAccess.ReadPermissions;
        if (!await AuthorizedAsync(required, actor, original, token).ConfigureAwait(false)) return null;
        var libraries = await classification.ReadLibrariesAsync(origin.Dependencies.Select(d => d.KnowledgeBaseId)
            .Concat(origin.Graph.Nodes.Select(node => node.Status.KnowledgeBaseId)).Distinct().ToArray(), actor, token).ConfigureAwait(false);
        if (libraries is null) return null;
        var result = new List<AiKnowledgeFaqParentAuthorization>();
        foreach (var node in origin.Graph.Nodes)
        {
            var read = await nodes.ReadAsync(node, actor, token, verifyPayloads, libraries, AiKnowledgeFaqGraphs.Ancestors(origin.Graph, node)).ConfigureAwait(false);
            if (read is null || !AiKnowledgeFaqPublishedSourceReader.Same(node.Source, read.Source with { Graph = null })
                || !AiKnowledgeFaqGraphs.Same(AiKnowledgeFaqGraphs.Ancestors(origin.Graph, node), read.Source.Graph)) return null;
            result.Add(new(node, read.Model, Domain.AiKnowledgeFaqModelFingerprint.Create(read.Model), read.Knowledge, read.Libraries, read.Publication));
        }
        if (!await AuthorizedAsync(required, actor, original, token).ConfigureAwait(false)) return null;
        if (verifyBusiness && origin.PersonalSchedules is { } finalBusiness && (personalSchedules is null
            || !await personalSchedules.RevalidateAsync(actor, finalBusiness, token, original).ConfigureAwait(false))) return null;
        return result;
    }
    /// <summary>模型/数据库等待后的末次业务权威复核；所有跨模块调用都留在本地事务外。</summary>
    internal async Task<bool> BusinessAsync(AiKnowledgeFaqProvenance origin, Guid actor, CancellationToken token, SessionBindingSnapshot? original = null)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("FAQ业务来源不能加入本地事务。");
        return origin.PersonalSchedules is null || personalSchedules is not null
            && await personalSchedules.RevalidateAsync(actor, origin.PersonalSchedules, token, original).ConfigureAwait(false);
    }
    private async Task<bool> AuthorizedAsync(IReadOnlyList<string> required, Guid actor, SessionBindingSnapshot? original, CancellationToken token)
    {
        if (original is null) return permissions is not null && await permissions.IsAuthorizedAsync(required, actor, token).ConfigureAwait(false);
        // 后台使用任务原会话，不伪造 HttpContext，也不将上传者身份当作当前授权。
        if (background is null || original.UserId != actor || original.TenantId != tenant.Id) return false;
        var result = await background.AuthorizeAllAsync(original, required, token).ConfigureAwait(false);
        return result?.UserId == actor && result.SessionId == original.SessionId && result.TenantId == original.TenantId;
    }
}

/// <summary>最终查询只接受服务器读取的草稿头与来源身份。</summary>
internal sealed record AiKnowledgeFaqDraftBinding(Guid Id, string SourceHash, Guid HeadRevisionId, int HeadVersion);
