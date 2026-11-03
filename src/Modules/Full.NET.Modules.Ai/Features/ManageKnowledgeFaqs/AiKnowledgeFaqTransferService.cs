using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>导出整包核证，导入逐条事务；包内正文只用于比对服务器修订，绝不作为来源权威。</summary>
internal sealed partial class AiKnowledgeFaqDraftService
{
    internal Task<Result<AiKnowledgeFaqTransferPackage>> ExportAsync(Guid knowledge, Guid actor, ExportAiKnowledgeFaqs request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Export, async (context, work) =>
        {
            if (request.FaqIds is not { Count: >= 1 and <= AiKnowledgeFaqTransferPolicy.MaximumItems } || request.FaqIds.Any(id => id == Guid.Empty)
                || request.FaqIds.Distinct().Count() != request.FaqIds.Count) return Invalid<AiKnowledgeFaqTransferPackage>();
            var rootParameters = Parameters(knowledge, context); rootParameters["Id"] = knowledge;
            var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(Choose(context, AiKnowledgeSql.FindHost, AiKnowledgeSql.FindTenant), rootParameters, work).ConfigureAwait(false); Check(context);
            if (root is not { IsEnabled: true }) return Missing<AiKnowledgeFaqTransferPackage>();
            var rows = new List<AiKnowledgeFaqDraftRecord>();
            foreach (var id in request.FaqIds)
            {
                var row = await Read(knowledge, id, context, work).ConfigureAwait(false);
                if (row is null) return Missing<AiKnowledgeFaqTransferPackage>();
                if (!await Exportable(row, context, work).ConfigureAwait(false)) return Unavailable<AiKnowledgeFaqTransferPackage>();
                rows.Add(row);
            }
            // 全部等待完成后统一复核，不能逐条核证后直接拼出含旧授权正文的包。
            var proven = await sources.AuthorizeAsync(rows, actor, AiKnowledgeFaqPermissions.Export, work, exportRoot: root).ConfigureAwait(false); Check(context);
            if (rows.Any(row => !proven.Contains(row.Id))) return Unavailable<AiKnowledgeFaqTransferPackage>();
            var package = new AiKnowledgeFaqTransferPackage(AiKnowledgeFaqTransferPolicy.Format, rows.Select(row => AiKnowledgeFaqTransferPolicy.From(Map(row))).ToArray());
            _ = AiKnowledgeFaqTransferPolicy.Digest(package); return Result<AiKnowledgeFaqTransferPackage>.Success(package);
        }, token);

    internal Task<Result<AiKnowledgeFaqImportReport>> PreviewImportAsync(Guid knowledge, Guid actor, PreviewAiKnowledgeFaqImport request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Import, async (context, work) =>
        {
            var digest = AiKnowledgeFaqTransferPolicy.Digest(request.Package);
            await Authorize(context, AiKnowledgeFaqPermissions.Write, work).ConfigureAwait(false);
            var target = await TransferKnowledge(knowledge, context, work).ConfigureAwait(false);
            if (target is not { IsEnabled: true }) return Missing<AiKnowledgeFaqImportReport>();
            var results = new List<AiKnowledgeFaqImportItemResult>();
            for (var i = 0; i < request.Package.Items.Count; i++)
            {
                var current = await ImportSource(request.Package.Items[i], target, context, work).ConfigureAwait(false);
                results.Add(new(i, current.Status, null, null));
            }
            await Authorize(context, AiKnowledgeFaqPermissions.Import, work).ConfigureAwait(false);
            return Result<AiKnowledgeFaqImportReport>.Success(new(digest, results));
        }, token);

    internal Task<Result<AiKnowledgeFaqImportReport>> ImportAsync(Guid knowledge, Guid actor, ImportAiKnowledgeFaqs request, CancellationToken token) =>
        Run(actor, AiKnowledgeFaqPermissions.Import, async (context, work) =>
        {
            var digest = AiKnowledgeFaqTransferPolicy.Digest(request.Package);
            await Authorize(context, AiKnowledgeFaqPermissions.Write, work).ConfigureAwait(false);
            if (digest != request.ExpectedDigest || request.RequestKeys is null || request.RequestKeys.Count != request.Package.Items.Count
                || request.RequestKeys.Any(id => id == Guid.Empty) || request.RequestKeys.Distinct().Count() != request.RequestKeys.Count) return Invalid<AiKnowledgeFaqImportReport>();
            var target = await TransferKnowledge(knowledge, context, work).ConfigureAwait(false);
            if (target is not { IsEnabled: true }) return Missing<AiKnowledgeFaqImportReport>();
            var results = new List<AiKnowledgeFaqImportItemResult>();
            for (var i = 0; i < request.Package.Items.Count; i++)
            {
                await Authorize(context, AiKnowledgeFaqPermissions.Import, work).ConfigureAwait(false);
                var item = request.Package.Items[i]; var candidate = await ImportSource(item, target, context, work).ConfigureAwait(false);
                if (candidate.Row is not { } original || candidate.Approval is null) { results.Add(new(i, candidate.Status, null, null)); continue; }
                var saved = await Create(knowledge, context, request.RequestKeys[i], original.ContentJson, AiKnowledgeFaqProvenanceCodec.Decode(original),
                    new(original.KnowledgeBaseId, original.Id, original.HeadRevisionId, original.HeadVersion, candidate.Approval), work).ConfigureAwait(false);
                if (saved.IsSuccess) results.Add(new(i, saved.Value!.Replayed ? "replayed" : "created", saved.Value.Workspace.Id, saved.Value.SavedRevisionId));
                else results.Add(new(i, saved.Error!.Code == "ai.knowledge_faqs.version_conflict" ? "conflict" : "source_unavailable", null, null));
            }
            await Authorize(context, AiKnowledgeFaqPermissions.Import, work).ConfigureAwait(false);
            return Result<AiKnowledgeFaqImportReport>.Success(new(digest, results));
        }, token);

    private async Task<(AiKnowledgeFaqDraftRecord? Row, TransferApproval? Approval, string Status)> ImportSource(AiKnowledgeFaqTransferItem item, AiKnowledgeBaseRecord target, Context context, CancellationToken token)
    {
        var source = await TransferKnowledge(item.KnowledgeBaseId, context, token).ConfigureAwait(false);
        var current = await Read(item.KnowledgeBaseId, item.FaqId, context, token).ConfigureAwait(false);
        if (source is not { IsEnabled: true } || current is null || !AiKnowledgeFaqTransferPolicy.Same(item, AiKnowledgeFaqTransferPolicy.From(Map(current)))
            || !await Proven([current], context, AiKnowledgeFaqPermissions.Import, token).ConfigureAwait(false)) return (null, null, "source_unavailable");
        var classification = await transferInput.ClassificationAsync(current, context.Actor, token).ConfigureAwait(false); Check(context);
        if (classification is null) return (null, null, "source_unavailable");
        // 跨库复制不能借低分类容器丢掉独立来源的分类，也不能把提高分类的权限隐式授予导入操作。
        if (!AiKnowledgeFaqTransferPolicy.CanPlace(classification, target.DataClassification, source.Id == target.Id)) return (null, null, "classification_conflict");
        return (current, new(source.Version, source.DataClassification, target.Version, target.DataClassification, classification), "ready");
    }

    private async Task<AiKnowledgeBaseRecord?> TransferKnowledge(Guid id, Context context, CancellationToken token, bool inside = false)
    {
        var p = Parameters(id, context); p["Id"] = id;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(Choose(context, AiKnowledgeSql.FindHost, AiKnowledgeSql.FindTenant), p, token).ConfigureAwait(false);
        Check(context, inside); return row;
    }

    private async Task<bool> Exportable(AiKnowledgeFaqDraftRecord row, Context context, CancellationToken token)
    {
        var classification = await transferInput.ClassificationAsync(row, context.Actor, token).ConfigureAwait(false); Check(context);
        return classification is not null && processing.Allows("export", classification);
    }
}
