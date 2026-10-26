using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>一次提交的个人选票只引用本人上传文件，事务外认领，事务内落事实，事务后精确对账。</summary>
internal sealed class WorkflowVoteAttachmentCoordinator(IHostFileReferenceClaimService claims,
    IHostFileDescriptorReader descriptors, ITenantResourceFileDescriptorReader tenantDescriptors,
    IQueryExecutor query, ICommandExecutor command, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    ILogger<WorkflowVoteAttachmentCoordinator> logger)
{
    public async Task<Result<WorkflowVoteAttachmentPlan>> PrepareAsync(WorkflowVoteBallotRecord ballot, Guid instanceId,
        Guid actor, string scope, WorkflowFormSchema schema, JsonElement values, string evidenceHash, CancellationToken token)
    {
        if (WorkflowManagementScope.Resolve(tenant).TenantScopeKey != scope || actor == Guid.Empty ||
            ballot.AssigneeUserId != actor || ballot.StatusKey != "pending" || ballot.Revision < 1 ||
            !WorkflowFormValueValidator.Validate(schema, values)) return Invalid();
        var references = new List<WorkflowPreparedAttachment>();
        var plan = new WorkflowVoteAttachmentPlan(ballot.Id, ballot.TodoId, instanceId, actor, scope, ballot.Revision, evidenceHash, references);
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var answers = values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        try
        {
            foreach (var (key, files) in WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, answers))
            {
                if (!WorkflowFormAttachmentConstraints.TryRead(fields[key], out _, out var maximum, out var extensions)) return await ReleaseInvalid();
                foreach (var file in files)
                {
                    string name; long size; Guid? owner;
                    if (scope == "host")
                    {
                        var descriptor = await descriptors.GetReadyDescriptorAsync(file, token).ConfigureAwait(false);
                        if (descriptor?.CreatedByUserId != actor) return await ReleaseInvalid();
                        name = descriptor.OriginalFileName; size = descriptor.SizeBytes; owner = null;
                    }
                    else
                    {
                        var descriptor = await tenantDescriptors.GetReadyDescriptorAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey,
                            actor, file, token).ConfigureAwait(false);
                        if (descriptor?.CreatedByUserId != actor) return await ReleaseInvalid();
                        name = descriptor.OriginalFileName; size = descriptor.SizeBytes; owner = actor;
                    }
                    if (size <= 0 || size > maximum || !WorkflowFormAttachmentValueRules.MatchesAllowedExtension(name, extensions)) return await ReleaseInvalid();
                    var reference = new WorkflowPreparedAttachment(ids.NewId(), key, file, owner);
                    if (scope == "host")
                    {
                        var claim = await claims.ClaimAsync(new(HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(reference.Id, file),
                            HostFileReferenceClaimConsumerModules.Workflow, reference.Id, file), token).ConfigureAwait(false);
                        if (!claim.IsSuccess) return await ReleaseInvalid();
                    }
                    references.Add(reference);
                }
            }
            return Result<WorkflowVoteAttachmentPlan>.Success(plan);
        }
        catch { await FinishAsync(plan, token).ConfigureAwait(false); throw; }

        async Task<Result<WorkflowVoteAttachmentPlan>> ReleaseInvalid()
        { await FinishAsync(plan, token).ConfigureAwait(false); return Invalid(); }
    }

    public async Task<Result<bool>> WriteAsync(WorkflowVoteAttachmentPlan? plan, WorkflowVoteBallotRecord ballot,
        Guid instanceId, Guid actor, string scope, WorkflowFormSchema schema, JsonElement values, string evidenceHash, CancellationToken token)
    {
        var answers = values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        var files = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, answers);
        if (plan is null || plan.BallotId != ballot.Id || plan.TodoId != ballot.TodoId || plan.InstanceId != instanceId ||
            plan.ActorUserId != actor || ballot.AssigneeUserId != actor || plan.TenantScopeKey != scope ||
            plan.ExpectedRevision != ballot.Revision || plan.EvidenceHash != evidenceHash ||
            plan.Attachments.Count != files.Sum(item => item.Value.Length) ||
            plan.Attachments.Any(reference => !files.TryGetValue(reference.FieldKey, out var field) || !field.Contains(reference.FileId)))
            return Result<bool>.Failure(InvalidError());
        foreach (var reference in plan.Attachments)
        {
            if (await command.ExecuteAsync(WorkflowVoteAttachmentSql.Insert, WorkflowSqlParameters.Create(
                ("Id", reference.Id), ("BallotId", ballot.Id), ("TodoId", ballot.TodoId), ("InstanceId", instanceId),
                ("ActorUserId", actor), ("TenantScopeKey", scope), ("Revision", ballot.Revision + 1),
                ("FieldKey", reference.FieldKey), ("FileId", reference.FileId), ("OwnerResourceId", reference.OwnerResourceId),
                ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Result<bool>.Failure(InvalidError());
        }
        return Result<bool>.Success(true);
    }

    public async Task FinishAsync(WorkflowVoteAttachmentPlan? plan, CancellationToken token)
    {
        if (plan is null || plan.TenantScopeKey != "host") return;
        foreach (var reference in plan.Attachments)
        {
            try
            {
                var exists = await query.QuerySingleOrDefaultAsync<int>(WorkflowVoteAttachmentSql.ExistsForProbe,
                    WorkflowSqlParameters.Create(("ReferenceId", reference.Id), ("FileId", reference.FileId)), token).ConfigureAwait(false) == 1;
                var key = HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(reference.Id, reference.FileId);
                var succeeded = exists ? (await claims.ConfirmAsync(key, token).ConfigureAwait(false)).IsSuccess
                    : (await claims.ReleaseAsync(key, token).ConfigureAwait(false)).IsSuccess;
                if (!succeeded) logger.LogWarning("Workflow vote attachment claim awaits reconciliation for {ReferenceId}.", reference.Id);
            }
            catch (Exception exception) { logger.LogWarning(exception, "Workflow vote attachment claim awaits reconciliation for {ReferenceId}.", reference.Id); }
        }
    }

    private static Error InvalidError() => new(WorkflowErrorCodes.FormAttachmentInvalid, "The private vote attachment is invalid or unauthorized.", ErrorType.Validation);
    private static Result<WorkflowVoteAttachmentPlan> Invalid() => Result<WorkflowVoteAttachmentPlan>.Failure(InvalidError());
}

/// <summary>个人选票及完整签名内容的准备身份，不能由请求声明或跨待办复用。</summary>
internal sealed record WorkflowVoteAttachmentPlan(Guid BallotId, Guid TodoId, Guid InstanceId, Guid ActorUserId,
    string TenantScopeKey, long ExpectedRevision, string EvidenceHash, IReadOnlyList<WorkflowPreparedAttachment> Attachments);
