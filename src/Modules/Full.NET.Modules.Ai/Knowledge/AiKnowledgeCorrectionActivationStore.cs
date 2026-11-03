using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>人工完整代次的原子完成及归档；本类自己读取完整证明，不接受调用者提交的回执或成功标记。</summary>
internal sealed class AiKnowledgeCorrectionActivationStore(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, ICurrentTenant tenant, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database,
    AiKnowledgeIndexTaskScope tasks,
    AiKnowledgeCorrectionAuthority files, IBackgroundSessionAuthorization authorization)
{
    // 普通证明可能处在外层有界 IO 内，必须登记事务，取消时等待本地锁事务排空。
    private readonly ICommandTransaction proofTransactions = AiKnowledgeIndexPdfAuthorityBoundary.TrackTransactions(transaction);
    private const int MaxReceiptBytes = 1048576;
    private static readonly string[] Permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
        AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue,
        AiKnowledgeCorrectionPermissions.Read];

    /// <summary>全量验收在事务外完成；根锁内比较相同原字节、全部批次及成功账本，再原子完成、切换候选并归档。</summary>
    internal async Task<bool> ActivateAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        AiKnowledgeCorrectionIndexService planning, AiKnowledgeCorrectionVectorStore vectors, CancellationToken token)
    {
        Check(token); using var deadline = Deadline(token); var bounded = deadline.Token;
        if (await IsActiveAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false)) return true;
        var proof = await vectors.ReadWholePlanProofAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
        if (proof is null || !ValidReceipts(proof.Receipts, proof.Preparation.Plan)
            || await planning.ReadCurrentModelAsync(binding, taskId, leaseToken, proof.Preparation, bounded).ConfigureAwait(false) is null) return false;
        var prepared = proof.Preparation; var p = Fill(binding, taskId, leaseToken, prepared.Session, prepared.Plan, prepared.ManifestHash, proof.Receipts, bounded);
        p["Completed"] = 0; p["Id"] = ids.NewId();
        try
        {
            _ = await proofTransactions.ExecuteAsync(async ct =>
            {
                if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, tasks.IsTenant), p, ct).ConfigureAwait(false) != binding.DocumentId) return false;
                var source = await LockedAsync(binding, p, prepared.Session, prepared.Plan, proof.Receipts, prepared.Source, ct).ConfigureAwait(false);
                if (source is null || !Live(source)) return false;
                var previous = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.ExistingPointerHost, AiKnowledgeIndexActivationSql.ExistingPointerTenant), p, ct).ConfigureAwait(false);
                p["Now"] = clock.UtcNow;
                if (!Live(source) || await command.ExecuteAsync(Choose(AiKnowledgeCorrectionActivationSql.CompleteHost, AiKnowledgeCorrectionActivationSql.CompleteTenant), p, ct).ConfigureAwait(false) != 1) return false;
                // 第一笔写入以后所有拒绝必须回滚，不能留下孤立 completed 或仅切换指针。
                if (await command.ExecuteAsync(previous is null ? Choose(AiKnowledgeIndexActivationSql.InsertHost, AiKnowledgeIndexActivationSql.InsertTenant)
                    : Choose(AiKnowledgeIndexActivationSql.UpdateHost, AiKnowledgeIndexActivationSql.UpdateTenant), p, ct).ConfigureAwait(false) != 1) throw new RejectedException();
                if (await command.ExecuteAsync(Choose(AiKnowledgePublicationSql.ArchiveHost, AiKnowledgePublicationSql.ArchiveTenant), p, ct).ConfigureAwait(false) != 1) throw new RejectedException();
                ct.ThrowIfCancellationRequested(); if (!Live(source)) throw new RejectedException(); return true;
            }, bounded).ConfigureAwait(false);
            return await IsActiveAsync(binding, taskId, leaseToken, bounded).ConfigureAwait(false);
        }
        catch (RejectedException) { return false; }
    }

    /// <summary>完成态按原完成令牌及归档证明恢复，较新草稿不改变旧完整代次；原会话与 Files 仍须当前有效。</summary>
    internal Task<bool> IsActiveAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
        => VerifyAsync(binding, taskId, leaseToken, token);

    /// <summary>普通读者仅凭当前可见发布或管理候选读取；不要求原索引写入人的会话继续在线。</summary>
    internal async Task<bool> IsReadyForReadAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token)
    {
        Check(token);
        if (source.CorrectionRevisionId is not { } revision || revision == Guid.Empty || source.IsCurrent != 1 || source.IsDeleted != 0
            || source.CleanupPending != 0 || source.StateKey != "completed" || source.TaskId is not { } task || task == Guid.Empty
            || source.ActiveTaskId != task || source.ActiveLeaseToken is not { } completion || completion == Guid.Empty
            || !parameters.TryGetValue("OwnerUserId", out var actorValue) || actorValue is not Guid actor || actor == Guid.Empty) return false;
        var captured = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
        await using var held = await AiKnowledgeGenerationLease.AcquireAsync(source, actor, query, command, proofTransactions, tenant, clock, ids, database, token).ConfigureAwait(false);
        if (held is null) return false;
        var binding = new AiKnowledgeIndexTaskBinding(source.KnowledgeBaseId, source.DocumentId, source.VersionId, source.DocumentVersion, source.KnowledgeBaseVersion, source.OwnerUserId);
        return await VerifyAsync(binding, task, completion, held.Token, source, captured).ConfigureAwait(false) && held.IsValid;
    }

    private async Task<bool> VerifyAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token,
        AiKnowledgeIndexStatusRecord? expected = null, Dictionary<string, object?>? readParameters = null)
    {
        Check(token); using var deadline = Deadline(token); var bounded = deadline.Token;
        var p = tasks.HeldParameters(binding, taskId, leaseToken, bounded);
        // 正文读取之前先按当前读者复核两层资源权限及精确发布身份；写入人绑定不能授予读权。
        if (expected is not null && !await Visible(expected, readParameters!, bounded).ConfigureAwait(false)) return false;
        var pointer = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost, AiKnowledgeIndexActivationSql.PointerTenant), p, ct), bounded).ConfigureAwait(false);
        if (pointer is null || pointer.Id != taskId || pointer.IndexTaskId != taskId || pointer.DocumentId != binding.DocumentId || pointer.VersionId != binding.VersionId
            || pointer.LeaseToken != leaseToken || pointer.ReceiptJson is not { Length: <= MaxReceiptBytes }
            || Encoding.UTF8.GetByteCount(pointer.ReceiptJson) > MaxReceiptBytes || Hash(pointer.ReceiptJson) != pointer.ReceiptHash) return false;
        var sessionRow = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost, AiKnowledgeIndexActivationSql.SessionTenant), p, ct), bounded).ConfigureAwait(false);
        if (sessionRow is null || sessionRow.Id == Guid.Empty || sessionRow.IndexTaskId != taskId) return false; var session = Snapshot(sessionRow);
        if (expected is null && !await Authorized(binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return false;
        var metadata = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeVersionRecord>(AiKnowledgeCorrectionActivationSql.MetadataFor(tasks.IsTenant), p, ct), bounded).ConfigureAwait(false);
        if (metadata is null || metadata.Id != binding.VersionId || metadata.DocumentId != binding.DocumentId || metadata.FileId == Guid.Empty
            || metadata.SizeBytes is <= 0 or > int.MaxValue || metadata.ContentHash is not { Length: 64 } || !metadata.ContentHash.All(char.IsAsciiHexDigit)
            || AiKnowledgeDocumentFormats.Format(metadata.ContentType) is not { } format
            || AiKnowledgeDocumentFormats.Policy(metadata.ContentType, metadata.ParsingPolicyKey) is not { } policy
            || expected is not null && (metadata.FileId != expected.FileId || metadata.ContentHash != expected.ContentHash
                || metadata.SizeBytes != expected.SizeBytes || metadata.ContentType != expected.ContentType || metadata.ParsingPolicyKey != expected.ParsingPolicyKey)
            || !await files.FileReadyAsync(metadata, session, bounded).ConfigureAwait(false)
            || expected is null && !await Authorized(binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return false;
        var row = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(AiKnowledgeCorrectionActivationSql.ManifestFor(database.Value.Provider, tasks.IsTenant), p, ct), bounded).ConfigureAwait(false);
        if (row is null || row.Id != taskId || row.FormatKey != AiKnowledgeCorrectionManifestCodec.FormatKey || row.PayloadHash != pointer.ManifestHash
            || row.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionManifestCodec.MaxPayloadBytes }) return false;
        var owned = row.Payload.ToArray(); AiKnowledgeCorrectionIndexPlan plan;
        try
        {
            if (Convert.ToHexString(SHA256.HashData(owned)) != row.PayloadHash) return false;
            try { plan = AiKnowledgeCorrectionManifestCodec.Decode(owned, format, metadata.ContentHash, checked((int)metadata.SizeBytes), policy, bounded); }
            catch (InvalidDataException) { return false; }
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
        AiKnowledgeIndexVectorReceiptRecord[]? receipts;
        try { receipts = JsonSerializer.Deserialize(pointer.ReceiptJson, AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray); }
        catch (JsonException) { return false; }
        if (plan.PlanHash != pointer.PlanHash || !ValidReceipts(receipts, plan) || !ValidBinding(binding, plan)
            || expected is not null && (expected.CorrectionRevisionId != plan.Snapshot.Identity.RevisionId
                || expected.ActivePlanHash != pointer.PlanHash || expected.ActiveManifestHash != pointer.ManifestHash || expected.ActiveReceiptHash != pointer.ReceiptHash)) return false;
        p = Fill(binding, taskId, leaseToken, session, plan, row.PayloadHash, receipts!, bounded); p["Completed"] = 1;
        if ((string)p["ReceiptHash"]! != pointer.ReceiptHash) return false;
        // 已完成内容不能通过声明摘要蒙混过去，事务前仍逐批解析原字节并重算原请求指纹。
        foreach (var receipt in receipts!)
        {
            p["BatchOrdinal"] = receipt.BatchOrdinal;
            var payload = await ReadPayloadUnderRootAsync(binding, p, bounded).ConfigureAwait(false);
            if (!ValidatePayload(payload, taskId, receipt, plan, bounded)) return false;
        }
        var valid = await proofTransactions.ExecuteAsync(async ct =>
        {
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, tasks.IsTenant), p, ct).ConfigureAwait(false) != binding.DocumentId) return false;
            var source = await LockedAsync(binding, p, session, plan, receipts!, null, ct).ConfigureAwait(false);
            if (source is null || source.FileId != metadata.FileId) return false;
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost, AiKnowledgeIndexActivationSql.PointerTenant), p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return current == pointer && (expected is null || await Visible(expected, readParameters!, ct).ConfigureAwait(false));
        }, bounded).ConfigureAwait(false);
        // Files 元数据与原写入会话在线状态无关；读者授权由外层重验，仍需复核当前文件就绪性。
        if (!valid || !await files.FileReadyAsync(metadata, session, bounded).ConfigureAwait(false)
            || expected is null && !await Authorized(binding, taskId, leaseToken, session, bounded).ConfigureAwait(false)) return false;
        // 最后外部等待后再锁读同一 AI 权威和全部实际摘要，不能返回等待前的成功缓存。
        var finalProof = await proofTransactions.ExecuteAsync(async ct =>
        {
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, tasks.IsTenant), p, ct).ConfigureAwait(false) != binding.DocumentId) return false;
            var source = await LockedAsync(binding, p, session, plan, receipts!, null, ct).ConfigureAwait(false);
            var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Choose(AiKnowledgeIndexActivationSql.PointerHost, AiKnowledgeIndexActivationSql.PointerTenant), p, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return source is not null && source.FileId == metadata.FileId && current == pointer
                && (expected is null || await Visible(expected, readParameters!, ct).ConfigureAwait(false));
        }, bounded).ConfigureAwait(false);
        Check(bounded); return finalProof;
    }

    /// <summary>载荷预读也使用文档根锁序；单条 FOR UPDATE 不能在事务外先锁任务再等待文档。</summary>
    internal async Task<AiKnowledgeIndexVectorRecord?> ReadPayloadUnderRootAsync(AiKnowledgeIndexTaskBinding binding,
        Dictionary<string, object?> parameters, CancellationToken token)
    {
        Check(token);
        return await proofTransactions.ExecuteAsync<AiKnowledgeIndexVectorRecord?>(async ct =>
        {
            if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgePublicationSql.LockFor(database.Value.Provider, tasks.IsTenant), parameters, ct).ConfigureAwait(false) != binding.DocumentId)
                return null;
            return await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(PayloadStatement, parameters, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private async Task<bool> Visible(AiKnowledgeIndexStatusRecord expected, Dictionary<string, object?> readParameters, CancellationToken token)
    {
        var p = new Dictionary<string, object?>(readParameters, StringComparer.Ordinal) { ["KnowledgeBaseId"] = expected.KnowledgeBaseId,
            ["DocumentId"] = expected.DocumentId, ["VersionId"] = expected.VersionId, ["TaskId"] = expected.TaskId, ["PublicationId"] = expected.PublicationId };
        var actual = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgePublishedSourceSql.ForGeneration(database.Value.Provider, tasks.IsTenant), p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return actual == expected;
    }

    /// <summary>全量代次证明以后按原清单核对持久引用，防止结构合法的 JSON 篡改位置或内容性质。</summary>
    internal async Task<IReadOnlyList<AiKnowledgeAnswerReference>> MatchReferencesAsync(AiKnowledgeIndexStatusRecord source,
        Dictionary<string, object?> parameters, IReadOnlyList<AiKnowledgeAnswerReference> references, CancellationToken token)
    {
        Check(token); if (references.Count == 0) return []; if (references.Count > 512 || !await Visible(source, parameters, token).ConfigureAwait(false)) return [];
        var binding = new AiKnowledgeIndexTaskBinding(source.KnowledgeBaseId, source.DocumentId, source.VersionId, source.DocumentVersion, source.KnowledgeBaseVersion, source.OwnerUserId);
        if (source.TaskId is not { } task || source.ActiveLeaseToken is not { } completion || source.CorrectionRevisionId is null) return [];
        var p = tasks.HeldParameters(binding, task, completion, token);
        var row = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(AiKnowledgeCorrectionActivationSql.ManifestFor(database.Value.Provider, tasks.IsTenant), p, ct), token).ConfigureAwait(false);
        if (row is null || row.Id != task || row.FormatKey != AiKnowledgeCorrectionManifestCodec.FormatKey || row.PayloadHash != source.ActiveManifestHash
            || row.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionManifestCodec.MaxPayloadBytes }
            || Convert.ToHexString(SHA256.HashData(row.Payload)) != source.ActiveManifestHash || source.SizeBytes is <= 0 or > int.MaxValue
            || AiKnowledgeDocumentFormats.Format(source.ContentType) is not { } format || AiKnowledgeDocumentFormats.Policy(source.ContentType, source.ParsingPolicyKey) is not { } policy) return [];
        AiKnowledgeCorrectionIndexPlan plan;
        try { plan = AiKnowledgeCorrectionManifestCodec.Decode(row.Payload, format, source.ContentHash, checked((int)source.SizeBytes), policy, token); }
        catch (InvalidDataException) { return []; }
        if (plan.PlanHash != source.ActivePlanHash || plan.Snapshot.Identity.RevisionId != source.CorrectionRevisionId || !ValidBinding(binding, plan)) return [];
        var projected = new Dictionary<int, AiKnowledgeCorrectionCitationResponse>(); var accepted = new List<AiKnowledgeAnswerReference>();
        foreach (var reference in references)
        {
            token.ThrowIfCancellationRequested();
            if (reference.DocumentId != source.DocumentId || reference.VersionId != source.VersionId || reference.Sequence < 0 || reference.Sequence >= plan.Chunks.Count
                || reference.PageNumber is not null || reference.Offset is not null || reference.Length is not null || reference.SourceLocation is not null
                || reference.DocxLocation is not null || reference.Correction is not { } actual) continue;
            if (!projected.TryGetValue(reference.Sequence, out var expected))
                projected.Add(reference.Sequence, expected = AiKnowledgeCorrectionCitations.Create(plan, plan.Chunks[reference.Sequence], token));
            if (actual.RevisionId == expected.RevisionId && actual.TextLength == expected.TextLength && actual.Spans.SequenceEqual(expected.Spans)) accepted.Add(reference);
        }
        Check(token); return await Visible(source, parameters, token).ConfigureAwait(false) ? accepted.AsReadOnly() : [];
    }

    private async Task<AiKnowledgeIndexDispatchRecord?> LockedAsync(AiKnowledgeIndexTaskBinding binding, Dictionary<string, object?> p,
        SessionBindingSnapshot session, AiKnowledgeCorrectionIndexPlan plan, IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> expected,
        AiKnowledgeIndexDispatchRecord? previous, CancellationToken token)
    {
        // 撤销委托与激活串行化；锁顺序始终先文档根，再 AI 委托，再任务/载荷。
        var grants = await query.QueryAsync<Guid>(AiKnowledgeCorrectionActivationSql.GrantFor(database.Value.Provider, tasks.IsTenant), p, token).ConfigureAwait(false);
        if (grants.Count > 1) return null; p["Now"] = clock.UtcNow;
        var source = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(AiKnowledgeCorrectionActivationSql.AuthorityFor(database.Value.Provider, tasks.IsTenant), p, token).ConfigureAwait(false);
        if (source is null || source.TaskId != (Guid)p["TaskId"]! || source.DocumentId != binding.DocumentId || source.VersionId != binding.VersionId
            || AiKnowledgeIndexCorrectionInput.Identity(source) != plan.Snapshot.Identity || source.ModelConfigId != plan.Model.ModelConfigId
            || source.ModelConfigVersion != plan.Model.ModelConfigVersion || source.ModelProviderKey != plan.Model.ProviderKey || source.ModelId != plan.Model.ModelId
            || source.DataClassification != plan.Model.DataClassification || AiKnowledgeDocumentModelChunker.Destination(source.ModelEndpointBaseUrl, source.ModelOrganizationId) != plan.Model.DestinationHash
            || source.SizeBytes != plan.Snapshot.Document.Source.SizeBytes || !string.Equals(source.ContentHash, plan.Snapshot.Document.Source.ContentHash, StringComparison.OrdinalIgnoreCase)
            || source.ContentType != AiKnowledgeDocumentFormats.ContentType(plan.Snapshot.Document.Source) || source.ParsingPolicyKey != AiKnowledgeDocumentFormats.PolicyKey(plan.Snapshot.Document.Source)
            || previous is not null && (!AiKnowledgeIndexEmbeddingService.SameSource(previous, source) || !Live(source))) return null;
        // 清单、批次和向量即使自洽，也必须重新匹配真正收费的人工请求，不能借用自动原文回执。
        var model = new AiModelConfigRecord { Id = source.ModelConfigId, Version = source.ModelConfigVersion,
            ProviderKey = source.ModelProviderKey, ModelId = source.ModelId, EndpointBaseUrl = source.ModelEndpointBaseUrl, OrganizationId = source.ModelOrganizationId };
        for (var ordinal = 0; ordinal < expected.Count; ordinal++)
            if (expected[ordinal].RequestHash != AiOperationBudgetStore.Fingerprint(AiKnowledgeCorrectionBudgetRequest.Create(expected[ordinal].Id, model, plan, ordinal))) return null;
        var currentSession = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost, AiKnowledgeIndexActivationSql.SessionTenant), p, token).ConfigureAwait(false);
        if (currentSession is null || Snapshot(currentSession) != session) return null;
        if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeCorrectionActivationSql.ManifestFor(database.Value.Provider, tasks.IsTenant, true), p, token).ConfigureAwait(false) != source.TaskId) return null;
        var batches = await query.QueryAsync<AiKnowledgeIndexBatchRecord>(BatchStatement, p, token).ConfigureAwait(false);
        if (batches.Count != expected.Count || !batches.Select((b, index) => b.Id == expected[index].Id && b.BatchOrdinal == index
            && b.RequestHash == expected[index].RequestHash && b.InputCount == expected[index].InputCount).All(x => x)) return null;
        var receipts = await query.QueryAsync<AiKnowledgeIndexVectorReceiptRecord>(ReceiptStatement, p, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return expected.SequenceEqual(receipts) ? source : null;
    }

    private bool ValidatePayload(AiKnowledgeIndexVectorRecord? row, Guid taskId, AiKnowledgeIndexVectorReceiptRecord expected,
        AiKnowledgeCorrectionIndexPlan plan, CancellationToken token)
    {
        if (row is null || row.IndexTaskId != taskId || row.Id != expected.Id || Receipt(row) != expected
            || row.Payload is not { Length: >= 8 and <= AiKnowledgeCorrectionVectorCodec.MaxPayloadBytes }) return false;
        AiKnowledgeCorrectionVectorBatch payload;
        try { payload = AiKnowledgeCorrectionVectorCodec.ReadBatch(plan, expected.BatchOrdinal, row.Payload, token); }
        catch (InvalidDataException) { return false; }
        if (payload.PayloadHash != expected.PayloadHash) return false;
        return row.ModelConfigId == plan.Model.ModelConfigId && row.ModelConfigVersion == plan.Model.ModelConfigVersion;
    }

    private Dictionary<string, object?> Fill(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session,
        AiKnowledgeCorrectionIndexPlan plan, string manifestHash, IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord> receipts, CancellationToken token)
    {
        if (!ValidBinding(binding, plan) || !ValidReceipts(receipts, plan)) throw new InvalidDataException("Invalid correction generation proof.");
        var p = tasks.HeldParameters(binding, taskId, leaseToken, token); var model = plan.Model; var source = plan.Snapshot.Document.Source; var identity = plan.Snapshot.Identity;
        p["SessionId"] = session.SessionId; p["SessionKind"] = session.SessionKind; p["SecurityStamp"] = session.SecurityStamp; p["ActorScope"] = session.ActorScope; p["EffectiveScope"] = session.EffectiveScope;
        p["ModelConfigId"] = model.ModelConfigId; p["ModelConfigVersion"] = model.ModelConfigVersion; p["ProviderKey"] = model.ProviderKey; p["ModelId"] = model.ModelId;
        p["ContentHash"] = source.ContentHash; p["ContentType"] = AiKnowledgeDocumentFormats.ContentType(source); p["ParsingPolicyKey"] = AiKnowledgeDocumentFormats.PolicyKey(source); p["SizeBytes"] = source.SizeBytes;
        p["CorrectionSourceId"] = identity.SourceId; p["CorrectionRevisionId"] = identity.RevisionId; p["CorrectionHeadVersion"] = identity.HeadVersion; p["CorrectionSourceHash"] = identity.SourceHash; p["CorrectionPayloadHash"] = identity.PayloadHash;
        p["ParserRuntimeId"] = plan.Snapshot.ParserRuntimeId; p["PlanHash"] = plan.PlanHash; p["ManifestHash"] = manifestHash; p["ScopeKey"] = session.TenantId?.ToString("N") ?? "host";
        var original = AiKnowledgeParsedWireCodec.Encode(source, token);
        try { p["OriginalPayloadHash"] = Convert.ToHexString(SHA256.HashData(original)); } finally { CryptographicOperations.ZeroMemory(original); }
        var json = JsonSerializer.Serialize(receipts.ToArray(), AiKnowledgeIndexActivationJsonContext.Default.AiKnowledgeIndexVectorReceiptRecordArray);
        if (Encoding.UTF8.GetByteCount(json) > MaxReceiptBytes) throw new InvalidDataException("Correction receipts exceed the fixed limit.");
        p["ReceiptJson"] = json; p["ReceiptHash"] = Hash(json); return p;
    }
    private bool ValidBinding(AiKnowledgeIndexTaskBinding binding, AiKnowledgeCorrectionIndexPlan plan) => plan.Model.KnowledgeBaseId == binding.KnowledgeBaseId
        && plan.Model.KnowledgeBaseVersion == binding.KnowledgeBaseVersion && plan.Model.TenantId == tenant.Id;
    private static bool ValidReceipts(IReadOnlyList<AiKnowledgeIndexVectorReceiptRecord>? rows, AiKnowledgeCorrectionIndexPlan plan) => rows is { Count: >= 1 and <= 2048 }
        && rows.Count == plan.Batches.Count && rows.All(r => r is not null) && rows.Select(r => r.Id).Distinct().Count() == rows.Count
        && rows.Select((r, i) => r.Id != Guid.Empty && r.BatchOrdinal == i && r.InputCount == plan.Batches[i].Chunks.Count
            && Digest(r.RequestHash) && Digest(r.PayloadHash) && r.ProviderKey == plan.Model.ProviderKey && r.ModelId == plan.Model.ModelId
            && r.Dimensions == AiKnowledgeCorrectionVectorCodec.Dimensions(plan) && r.DistanceKey == "cosine" && r.FormatKey == AiKnowledgeCorrectionVectorCodec.FormatKey).All(x => x);
    private async Task<bool> Authorized(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, SessionBindingSnapshot session, CancellationToken token)
    {
        if (session.UserId != binding.OwnerUserId || session.TenantId != tenant.Id || session.SessionId == Guid.Empty || session.SessionKind is not (SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication)) return false;
        var row = await Read(ct => query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Choose(AiKnowledgeIndexActivationSql.SessionHost, AiKnowledgeIndexActivationSql.SessionTenant), tasks.HeldParameters(binding, taskId, leaseToken, ct), ct), token).ConfigureAwait(false);
        if (row is null || row.Generation < 0 || Snapshot(row) != session) return false;
        string[] required = row.Generation > 0 ? [.. Permissions, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexRebuild] : Permissions;
        var actor = await Read(ct => authorization.AuthorizeAllAsync(session, required, ct), token).ConfigureAwait(false);
        return actor?.UserId == session.UserId && actor.TenantId == session.TenantId && actor.SessionId == session.SessionId;
    }
    private bool Live(AiKnowledgeIndexDispatchRecord source) => source.LeaseExpiresAtUtc > clock.UtcNow && (source.GrantExpiresAtUtc is null || source.GrantExpiresAtUtc > clock.UtcNow);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static SessionBindingSnapshot Snapshot(AiKnowledgeIndexSessionRecord row) => new(row.ActorUserId, row.TenantId, row.SessionId, row.SecurityStamp, row.ActorScope, row.EffectiveScope, row.SessionKind);
    private static AiKnowledgeIndexVectorReceiptRecord Receipt(AiKnowledgeIndexVectorRecord row) => new() { Id = row.Id, BatchOrdinal = row.BatchOrdinal, RequestHash = row.RequestHash, InputCount = row.InputCount,
        PayloadHash = row.PayloadHash, ProviderKey = row.ProviderKey, ModelId = row.ModelId, Dimensions = row.Dimensions, FormatKey = row.FormatKey, DistanceKey = row.DistanceKey };
    private SqlStatement Choose(SqlStatement host, SqlStatement scoped) => tasks.IsTenant ? scoped : host;
    private SqlStatement BatchStatement => database.Value.Provider == DatabaseProvider.MySql ? Choose(AiKnowledgeIndexActivationSql.BatchesMySqlHost, AiKnowledgeIndexActivationSql.BatchesMySqlTenant) : Choose(AiKnowledgeIndexActivationSql.BatchesSqlServerHost, AiKnowledgeIndexActivationSql.BatchesSqlServerTenant);
    private SqlStatement ReceiptStatement => database.Value.Provider == DatabaseProvider.MySql ? Choose(AiKnowledgeIndexDocumentActivationSql.ReceiptsMySqlHost, AiKnowledgeIndexDocumentActivationSql.ReceiptsMySqlTenant) : Choose(AiKnowledgeIndexDocumentActivationSql.ReceiptsSqlServerHost, AiKnowledgeIndexDocumentActivationSql.ReceiptsSqlServerTenant);
    private SqlStatement PayloadStatement => database.Value.Provider == DatabaseProvider.MySql ? Choose(AiKnowledgeIndexDocumentActivationSql.PayloadMySqlHost, AiKnowledgeIndexDocumentActivationSql.PayloadMySqlTenant) : Choose(AiKnowledgeIndexDocumentActivationSql.PayloadSqlServerHost, AiKnowledgeIndexDocumentActivationSql.PayloadSqlServerTenant);
    private async Task<T> Read<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    { Check(token); var result = await AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(action, token).ConfigureAwait(false); Check(token); return result; }
    private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); if (transactions.HasTransaction) throw new InvalidOperationException("Correction activation cannot join an ambient transaction."); }
    private static CancellationTokenSource Deadline(CancellationToken token) { var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(25)); return deadline; }
    private sealed class RejectedException : Exception;
}
