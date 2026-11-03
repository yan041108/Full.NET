using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>冻结修订、物化数据库来源并共享原索引任务/会话；不生成文件，不自动替用户确认费用。</summary>
internal sealed class AiKnowledgeFaqIndexService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IIdGenerator ids, AiKnowledgeFaqLifecycleAccess gate, AiKnowledgeFaqIndexInput input, AiKnowledgeIndexSessionService sessions)
{
    internal async Task<Result<AiKnowledgeFaqIndexAdmission>> IndexAsync(Guid knowledge, Guid faq, IndexAiKnowledgeFaqRevision request,
        SessionBindingSnapshot actor, CancellationToken token)
    {
        if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Index, token, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue).ConfigureAwait(false))
            return Fail<AiKnowledgeFaqIndexAdmission>("authorization.permission_denied", ErrorType.Forbidden);
        try
        {
            AiKnowledgeFaqDraftPolicy.Key(request.RequestKey); AiKnowledgeFaqDraftPolicy.Key(request.ExpectedRevisionId);
            if (!request.ConfirmCost || request.ExpectedHeadVersion is < 1 or int.MaxValue) return Fail<AiKnowledgeFaqIndexAdmission>("ai.knowledge_faqs.input_invalid", ErrorType.Validation);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(90)); var ct = deadline.Token;
            var p = await gate.ParametersAsync(knowledge, faq, actor, ct).ConfigureAwait(false);
            var root = await gate.ReadAsync(p, null, ct, true).ConfigureAwait(false);
            if (root is null) return AiKnowledgeFaqLifecycleAccess.Missing<AiKnowledgeFaqIndexAdmission>();
            if (root.HeadRevisionId != request.ExpectedRevisionId || root.HeadVersion != request.ExpectedHeadVersion) return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            var content = AiKnowledgeFaqLifecycleAccess.Content(root);
            if (content.ExpiresAtUtc is { } expires && expires <= gate.Now) return Fail<AiKnowledgeFaqIndexAdmission>("ai.knowledge_faqs.input_invalid", ErrorType.Validation);
            if (!await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Index, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqIndexAdmission>();
            var classification = await input.ClassificationAsync(root, actor.UserId, ct).ConfigureAwait(false);
            if (classification is null) return SourceMissing<AiKnowledgeFaqIndexAdmission>();
            p["Id"] = knowledge;
            var kb = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(gate.Choose(AiKnowledgeSql.FindHost, AiKnowledgeSql.FindTenant), p, ct).ConfigureAwait(false);
            if (kb is null || !kb.IsEnabled || kb.OwnerUserId != actor.UserId || kb.EmbeddingModelConfigId is not { } modelId || kb.EmbeddingModelVersion is not { } modelVersion)
                return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            p["ModelConfigId"] = modelId;
            var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(gate.Choose(AiModelConfigSql.FindAvailableForHostChat, AiModelConfigSql.FindAvailableForTenantChat), p, ct).ConfigureAwait(false);
            if (model is null || !model.IsEnabled || model.Version != modelVersion) return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            var body = AiKnowledgeFaqIndexContent.Create(content); var modelFingerprint = AiKnowledgeFaqModelFingerprint.Create(model);
            p["ModelConfigId"] = modelId; p["ModelConfigVersion"] = modelVersion; p["ModelFingerprint"] = modelFingerprint;
            var hash = AiKnowledgeFaqDraftPolicy.Hash(FormattableString.Invariant($"faq_index_v1\n{knowledge:N}\n{faq:N}\n{request.ExpectedRevisionId:N}\n{request.ExpectedHeadVersion}\n{root.ContentHash}\n{root.SourceHash}\n{actor.UserId:N}\n{kb.Version}\n{modelId:N}\n{modelVersion}\n{modelFingerprint}\n{classification}\nconfirmed"));
            p["RequestKey"] = request.RequestKey; p["RequestHash"] = hash; p["RevisionId"] = root.RevisionId;
            var existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(gate.Choose(AiKnowledgeFaqIndexSql.KeyHost, AiKnowledgeFaqIndexSql.KeyTenant), p, ct).ConfigureAwait(false);
            var replayed = existing is not null;
            if (existing is null)
            {
                var other = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(gate.Choose(AiKnowledgeFaqIndexSql.RevisionHost, AiKnowledgeFaqIndexSql.RevisionTenant), p, ct).ConfigureAwait(false);
                if (other is not null) return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
                p["VersionId"] = ids.NewId(); p["Title"] = content.Question; p["RevisionHash"] = root.ContentHash;
                p["ExpectedHeadVersion"] = root.HeadVersion; p["KnowledgeBaseVersion"] = kb.Version; p["DataClassification"] = classification;
                p["BodyText"] = body.Text; p["BodyHash"] = body.Hash; p["SizeBytes"] = body.SizeBytes; p["Now"] = gate.Now;
                var stored = await transaction.ExecuteResultAsync(async tx =>
                {
                    if (await query.QuerySingleOrDefaultAsync<Guid>(AiKnowledgeFaqDraftSql.LockFor(gate.Provider, gate.Scoped), p, tx).ConfigureAwait(false) != knowledge)
                        return AiKnowledgeFaqLifecycleAccess.Conflict<bool>();
                    var current = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(gate.Choose(AiKnowledgeFaqDraftSql.ReadHost, AiKnowledgeFaqDraftSql.ReadTenant), p, tx).ConfigureAwait(false);
                    if (current is null || current.HeadRevisionId != root.HeadRevisionId || current.HeadVersion != root.HeadVersion || current.SourceHash != root.SourceHash)
                        return AiKnowledgeFaqLifecycleAccess.Conflict<bool>();
                    _ = await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqIndexSql.DocumentHost, AiKnowledgeFaqIndexSql.DocumentTenant), p, tx).ConfigureAwait(false);
                    if (await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqIndexSql.UpdateDocumentHost, AiKnowledgeFaqIndexSql.UpdateDocumentTenant), p, tx).ConfigureAwait(false) != 1
                        || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqIndexSql.InsertHost, AiKnowledgeFaqIndexSql.InsertTenant), p, tx).ConfigureAwait(false) != 1
                        || await command.ExecuteAsync(gate.Choose(AiKnowledgeFaqIndexSql.VersionHost, AiKnowledgeFaqIndexSql.VersionTenant), p, tx).ConfigureAwait(false) != 1)
                        return AiKnowledgeFaqLifecycleAccess.Conflict<bool>();
                    return Result<bool>.Success(true);
                }, ct).ConfigureAwait(false);
                if (!stored.IsSuccess) return Result<AiKnowledgeFaqIndexAdmission>.Failure(stored.Error!);
                existing = await query.QuerySingleOrDefaultAsync<AiKnowledgeFaqIndexSourceRecord>(gate.Choose(AiKnowledgeFaqIndexSql.KeyHost, AiKnowledgeFaqIndexSql.KeyTenant), p, ct).ConfigureAwait(false);
            }
            if (existing is null || existing.ActorUserId != actor.UserId || existing.RequestHash != hash || existing.RevisionId != root.RevisionId
                || existing.RevisionHash != root.ContentHash || existing.SourceHash != root.SourceHash || existing.BodyText != body.Text
                || existing.BodyHash != body.Hash || existing.SizeBytes != body.SizeBytes || existing.DataClassification != classification || existing.KnowledgeBaseVersion != kb.Version || existing.ModelConfigId != modelId
                || existing.ModelConfigVersion != modelVersion || existing.ModelFingerprint != modelFingerprint)
                return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            var binding = new AiKnowledgeIndexTaskBinding(knowledge, root.ReservedDocumentId, existing.Id, root.HeadVersion, kb.Version, actor.UserId);
            if (!await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Index, true, ct).ConfigureAwait(false)
                || !await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Index, ct, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqIndexAdmission>();
            var taskId = await sessions.EnqueueFaqAsync(binding, existing, actor, ct).ConfigureAwait(false);
            if (taskId is null) return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            if (!await gate.ProveAsync(root, actor, AiKnowledgeFaqPermissions.Index, true, ct).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqIndexAdmission>();
            p["VersionId"] = existing.Id;
            var status = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.ForFaq(gate.Provider, gate.Scoped), p, ct).ConfigureAwait(false);
            if (status?.TaskId != taskId) return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>();
            var state = status.StateKey == "completed" ? await gate.CandidateAsync(root, existing.Id, p, actor, AiKnowledgeFaqPermissions.Index, true, ct).ConfigureAwait(false) is not null ? "ready" : "failed"
                : status.StateKey == "running" ? "processing" : status.StateKey ?? "pending_processing";
            if (!await gate.AuthorizedAsync(actor, AiKnowledgeFaqPermissions.Index, ct, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue).ConfigureAwait(false)) return SourceMissing<AiKnowledgeFaqIndexAdmission>();
            return Result<AiKnowledgeFaqIndexAdmission>.Success(new(faq, root.RevisionId, existing.Id, taskId.Value, state, replayed));
        }
        catch (UnauthorizedAccessException) { return Fail<AiKnowledgeFaqIndexAdmission>("authorization.permission_denied", ErrorType.Forbidden); }
        catch (DataCommandException error) when (error.Kind is DataCommandFailureKind.UniqueConstraint or DataCommandFailureKind.Deadlock) { return AiKnowledgeFaqLifecycleAccess.Conflict<AiKnowledgeFaqIndexAdmission>(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return Fail<AiKnowledgeFaqIndexAdmission>("ai.knowledge_faqs.input_invalid", ErrorType.Validation); }
    }
    private static Result<T> SourceMissing<T>() => Fail<T>("ai.knowledge_faqs.source_unavailable", ErrorType.Conflict);
    private static Result<T> Fail<T>(string code, ErrorType type) => AiKnowledgeFaqLifecycleAccess.Failure<T>(code, type);
}
