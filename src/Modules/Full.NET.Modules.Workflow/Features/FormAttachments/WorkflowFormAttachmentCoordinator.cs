using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>事务外准备 Files 所有权，事务内只追加实际历史引用，结束后探测本次身份确认或释放。</summary>
internal sealed class WorkflowFormAttachmentCoordinator(
    IHostFileReferenceClaimService claims, IHostFileDescriptorReader descriptors,
    IQueryExecutor query, ICommandExecutor command, IClock clock, IIdGenerator ids,
    ILogger<WorkflowFormAttachmentCoordinator> logger, ICurrentTenant tenant,
    ITenantResourceFileDescriptorReader tenantDescriptors)
{
    public Task<Result<WorkflowAttachmentPlan>> PrepareInitialAsync(Guid actorId, Guid submissionId,
        Guid instanceId, string tenantScopeKey, WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values, CancellationToken token,
        IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource>? reuseSources = null) =>
        PrepareAsync(actorId, submissionId, instanceId, tenantScopeKey, schema,
            new Dictionary<string, JsonElement>(), values, new HashSet<Guid>(), 0, Serialize(values), token, reuseSources);

    /// <summary>重新加入历史文件也须验证当前可见复用范围；旧引用存在本身不构成授权。</summary>
    public async Task<Result<WorkflowAttachmentPlan>> PrepareAsync(Guid actorId, Guid submissionId, Guid instanceId,
        string tenantScopeKey, WorkflowFormSchema schema, IReadOnlyDictionary<string, JsonElement> previous,
        IReadOnlyDictionary<string, JsonElement> next, IReadOnlySet<Guid> visibleFiles, long revision,
        string nextJson, CancellationToken token, IReadOnlyDictionary<Guid, WorkflowAttachmentReuseSource>? reuseSources = null)
    {
        if (WorkflowManagementScope.Resolve(tenant).TenantScopeKey != tenantScopeKey) return InvalidPlan();
        var oldFiles = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, previous);
        var nextFiles = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, next);
        var fields = schema.Sections.SelectMany(section => section.Fields).Where(field => field.FieldTypeKey == "attachment")
            .ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var prepared = new List<WorkflowPreparedAttachment>();
        var plan = new WorkflowAttachmentPlan(submissionId, instanceId, tenantScopeKey, actorId, revision,
            WorkflowSignatureCoordinator.HashSubmission(nextJson), prepared);
        try
        {
            foreach (var (fieldKey, fileIds) in nextFiles)
            {
                var added = fileIds.Where(file => !oldFiles.TryGetValue(fieldKey, out var old) || !old.Contains(file)).ToArray();
                if (added.Length == 0) continue;
                // 唯一键限定每个字段每个文件一行，读取规模受该字段最多十个候选文件约束。
                var existing = await query.QueryAsync<WorkflowAttachmentReferenceRecord>(WorkflowFormSubmissionAttachmentSql.ListForFieldFiles,
                    WorkflowSqlParameters.Create(("SubmissionId", submissionId), ("InstanceId", instanceId),
                        ("TenantScopeKey", tenantScopeKey), ("FieldKey", fieldKey), ("FileIds", added)), token).ConfigureAwait(false);
                foreach (var fileId in added)
                {
                    var descriptor = await ReadAuthorizedDescriptorAsync(actorId, instanceId, tenantScopeKey,
                        fileId, visibleFiles.Contains(fileId), token, reuseSources?.GetValueOrDefault(fileId)).ConfigureAwait(false);
                    if (descriptor is null || !WorkflowFormAttachmentConstraints.TryRead(fields[fieldKey], out _, out var maximum, out var extensions) ||
                        descriptor.SizeBytes <= 0 || descriptor.SizeBytes > maximum ||
                        !WorkflowFormAttachmentValueRules.MatchesAllowedExtension(descriptor.Name, extensions))
                    {
                        await FinishAsync(plan, token).ConfigureAwait(false);
                        return InvalidPlan();
                    }
                    if (existing.Any(row => row.FileId == fileId)) continue;
                    var reference = new WorkflowPreparedAttachment(ids.NewId(), fieldKey, fileId, descriptor.OwnerResourceId);
                    if (tenantScopeKey == "host")
                    {
                        var result = await claims.ClaimAsync(new(
                            HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(reference.Id, fileId),
                            HostFileReferenceClaimConsumerModules.Workflow, reference.Id, fileId), token).ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            await FinishAsync(plan, token).ConfigureAwait(false);
                            return InvalidPlan();
                        }
                    }
                    prepared.Add(reference);
                }
            }
            return Result<WorkflowAttachmentPlan>.Success(plan);
        }
        catch
        {
            await FinishAsync(plan, token).ConfigureAwait(false);
            throw;
        }
    }

    public Task WriteInitialAsync(WorkflowAttachmentPlan plan, CancellationToken token) => AppendAsync(plan, token);

    /// <summary>最终表单和准备身份不匹配时拒绝写入；此方法不调用任何 Files Contract。</summary>
    public async Task<Result<bool>> WritePreparedAsync(WorkflowAttachmentPlan? plan, Guid actorId,
        WorkflowFormSubmissionRecord submission, Guid instanceId, string tenantScopeKey, WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> previous, IReadOnlyDictionary<string, JsonElement> next,
        string nextJson, CancellationToken token)
    {
        var oldFiles = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, previous);
        var hasNewFile = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, next)
            .Any(field => field.Value.Any(file => !oldFiles.TryGetValue(field.Key, out var old) || !old.Contains(file)));
        if (!hasNewFile) return Result<bool>.Success(true);
        if (plan is null || plan.ActorId != actorId || plan.SubmissionId != submission.Id || plan.InstanceId != instanceId ||
            plan.TenantScopeKey != tenantScopeKey || plan.ExpectedRevision != submission.Revision ||
            plan.SubmissionHash != WorkflowSignatureCoordinator.HashSubmission(nextJson))
            return Result<bool>.Failure(InvalidError());
        await AppendAsync(plan, token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private async Task AppendAsync(WorkflowAttachmentPlan plan, CancellationToken token)
    {
        foreach (var reference in plan.Attachments)
        {
            var parameters = WorkflowSqlParameters.Create(("Id", reference.Id), ("SubmissionId", plan.SubmissionId),
                ("InstanceId", plan.InstanceId), ("FieldKey", reference.FieldKey), ("FileId", reference.FileId),
                ("TenantScopeKey", plan.TenantScopeKey), ("CreatedAtUtc", clock.UtcNow),
                ("ClaimReferenceId", reference.Id), ("OwnerResourceId", reference.OwnerResourceId));
            // 两个分支可以准备相同字段/文件，后取得实例锁的一方不插入重复事实，也不消费自己的临时 claim。
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForFieldFile, parameters, token).ConfigureAwait(false) == 1) continue;
            await command.ExecuteAsync(WorkflowFormSubmissionAttachmentSql.Insert, parameters, token).ConfigureAwait(false);
        }
    }

    public async Task FinishAsync(WorkflowAttachmentPlan? plan, CancellationToken token)
    {
        if (plan is null || plan.TenantScopeKey != "host") return;
        foreach (var reference in plan.Attachments)
        {
            var key = HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(reference.Id, reference.FileId);
            try
            {
                var exists = await query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForProbe,
                    WorkflowSqlParameters.Create(("ReferenceId", reference.Id), ("FileId", reference.FileId)), token).ConfigureAwait(false) == 1;
                var succeeded = exists ? (await claims.ConfirmAsync(key, token).ConfigureAwait(false)).IsSuccess
                    : (await claims.ReleaseAsync(key, token).ConfigureAwait(false)).IsSuccess;
                if (!succeeded) logger.LogWarning("Workflow attachment claim awaits reconciliation for {ReferenceId}.", reference.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Workflow attachment claim awaits reconciliation for {ReferenceId}.", reference.Id);
            }
        }
    }

    private async Task<AttachmentDescriptor?> ReadAuthorizedDescriptorAsync(Guid actorId, Guid instanceId, string scope,
        Guid fileId, bool canReuse, CancellationToken token, WorkflowAttachmentReuseSource? source = null)
    {
        WorkflowAttachmentFileOwnerRecord? projectedOwner = null;
        if (source is not null)
        {
            if (source.InstanceId == Guid.Empty || !WorkflowSubprocessPolicy.Key(source.FieldKey)) return null;
            var mapped = await query.QuerySingleOrDefaultAsync<WorkflowMappedAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindMappedFileOwner,
                WorkflowSqlParameters.Create(("InstanceId", source.InstanceId), ("TenantScopeKey", scope), ("FieldKey", source.FieldKey), ("FileId", fileId)), token).ConfigureAwait(false);
            // 旧附件表可能使用不区分大小写的排序规则；字段授权仍使用与Schema一致的Ordinal身份。
            if (mapped?.FieldKey != source.FieldKey) return null;
            projectedOwner = new(mapped.OwnerResourceId);
        }
        if (scope == "host")
        {
            var descriptor = await descriptors.GetReadyDescriptorAsync(fileId, token).ConfigureAwait(false);
            if (descriptor is null) return null;
            if (descriptor.CreatedByUserId != actorId && projectedOwner is null && (!canReuse || await FindOwnerAsync(instanceId, fileId, scope, token).ConfigureAwait(false) is null))
                return null;
            return new(descriptor.OriginalFileName, descriptor.SizeBytes, null);
        }
        var owner = actorId;
        var row = await tenantDescriptors.GetReadyDescriptorAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey, owner, fileId, token).ConfigureAwait(false);
        if (row is null && (canReuse || projectedOwner is not null))
        {
            var previousOwner = projectedOwner ?? await FindOwnerAsync(instanceId, fileId, scope, token).ConfigureAwait(false);
            if (previousOwner?.OwnerResourceId is not { } resourceId) return null;
            owner = resourceId;
            row = await tenantDescriptors.GetReadyDescriptorAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey, owner, fileId, token).ConfigureAwait(false);
        }
        return row is null || row.CreatedByUserId != owner ? null : new(row.OriginalFileName, row.SizeBytes, owner);
    }

    private Task<WorkflowAttachmentFileOwnerRecord?> FindOwnerAsync(Guid instanceId, Guid fileId, string scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindFileOwner,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("FileId", fileId), ("TenantScopeKey", scope)), token);

    private static string Serialize(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        Serialization.WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
    private static Error InvalidError() => new(WorkflowErrorCodes.FormAttachmentInvalid, "The workflow attachment is invalid or unauthorized.", ErrorType.Validation);
    private static Result<WorkflowAttachmentPlan> InvalidPlan() => Result<WorkflowAttachmentPlan>.Failure(InvalidError());
    private sealed record AttachmentDescriptor(string Name, long SizeBytes, Guid? OwnerResourceId);
}

/// <summary>事务外准备的单次文件持有身份，不能由客户端提供或跨办理复用。</summary>
internal sealed record WorkflowPreparedAttachment(Guid Id, string FieldKey, Guid FileId, Guid? OwnerResourceId);

/// <summary>仅由内核从真实父子关联及明确字段映射派生；不属于HTTP输入，仍逐项核对同作用域历史引用。</summary>
internal sealed record WorkflowAttachmentReuseSource(Guid InstanceId, string FieldKey);


/// <summary>准备绑定可信作用域、提交身份、期望修订和最终内容摘要。</summary>
internal sealed record WorkflowAttachmentPlan(Guid SubmissionId, Guid InstanceId, string TenantScopeKey,
    Guid ActorId, long ExpectedRevision, string SubmissionHash, IReadOnlyList<WorkflowPreparedAttachment> Attachments);
