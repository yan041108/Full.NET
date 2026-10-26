using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Domain;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>签名文件在事务外验证并预留；事务内只绑定不可变动作证据，结束后按权威事实确认。</summary>
internal sealed class WorkflowSignatureCoordinator(IHostFileDescriptorReader descriptors, IHostFileContentReader contents,
    IHostFileReferenceClaimService claims, IQueryExecutor query, ICommandExecutor command, IIdGenerator ids,
    IClock clock, ILogger<WorkflowSignatureCoordinator> logger, ICurrentTenant tenant,
    ITenantResourceFileDescriptorReader tenantDescriptors, ITenantResourceFileStore tenantFiles)
{
    internal const string SignatureOwnerModuleKey = "workflow";
    private const int MaximumBytes = 1024 * 1024;

    public Task<Result<WorkflowPreparedSignature?>> PrepareAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, Guid actorId, string actionKey, string idempotencyKey,
        Guid? fileId, long formRevision, string submissionJson, CancellationToken token) =>
        PrepareForFormAsync(todo, instance, actorId, actionKey, idempotencyKey, fileId,
            instance.FormVersionId ?? Guid.Empty, formRevision, submissionJson, token);

    /// <summary>签名可绑定实际独立投票表单，但版本必须来自该节点已发布且同作用域的固定绑定。</summary>
    public async Task<Result<WorkflowPreparedSignature?>> PrepareForFormAsync(WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, Guid actorId, string actionKey, string idempotencyKey,
        Guid? fileId, Guid formVersionId, long formRevision, string submissionJson, CancellationToken token)
    {
        if (fileId is null) return Result<WorkflowPreparedSignature?>.Success(null);
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantScopeKey != instance.TenantScopeKey ||
            !await IsFormBoundAsync(todo, instance, actionKey, formVersionId, token).ConfigureAwait(false)) return Invalid();
        var originalAssignee = await OriginalAssigneeAsync(todo, instance, token).ConfigureAwait(false);
        var descriptor = scope.TenantId is null
            ? await ReadHostDescriptorAsync(fileId.Value, token).ConfigureAwait(false)
            : await ReadTenantDescriptorAsync(actorId, fileId.Value, token).ConfigureAwait(false);
        if (descriptor is null || descriptor.CreatedByUserId != actorId || descriptor.SizeBytes is < 8 or > MaximumBytes ||
            descriptor.ContentType is not ("image/png" or "image/jpeg")) return Invalid();
        var content = scope.TenantId is null
            ? await OpenHostAsync(fileId.Value, token).ConfigureAwait(false)
            : await OpenTenantAsync(actorId, fileId.Value, token).ConfigureAwait(false);
        if (content is null) return Invalid();
        await using var stream = content;
        // 读取上限多一个字节，不能只信任 MIME、扩展名或声明长度。
        var buffer = new byte[(int)descriptor.SizeBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        if (total != descriptor.SizeBytes || total > MaximumBytes || !HasImageHeader(buffer, total, descriptor.ContentType)) return Invalid();
        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, total)));
        if (descriptor.ContentHash is { } expectedHash && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)) return Invalid();
        var id = ids.NewId();
        // 租户文件已经由 Files 以当前租户、模块和本人资源固化所有权，不能调用 Host claim 或切换身份。
        if (scope.TenantId is null)
        {
            var result = await claims.ClaimAsync(new(HostFileReferenceClaimIdempotencyKeys.WorkflowActionSignature(id),
                HostFileReferenceClaimConsumerModules.Workflow, id, fileId.Value), token).ConfigureAwait(false);
            if (!result.IsSuccess) return Invalid();
        }
        return Result<WorkflowPreparedSignature?>.Success(new(id, instance.Id, todo.StepId, todo.Id,
            instance.TenantScopeKey, actorId, originalAssignee, actionKey, idempotencyKey.Trim(),
            formVersionId, formRevision, HashSubmission(submissionJson), fileId.Value, hash));
    }

    public Task<Result<bool>> WriteAsync(WorkflowPreparedSignature? signature, WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, Guid actorId, string actionKey, string idempotencyKey,
        Guid? fileId, long formRevision, string submissionJson, CancellationToken token) =>
        WriteForFormAsync(signature, todo, instance, actorId, actionKey, idempotencyKey, fileId,
            instance.FormVersionId ?? Guid.Empty, formRevision, submissionJson, token);

    /// <summary>事务内复核表单绑定，不允许把主表单证据或其他票据的准备结果绑定到当前投票。</summary>
    public async Task<Result<bool>> WriteForFormAsync(WorkflowPreparedSignature? signature, WorkflowTodoRuntimeRecord todo,
        WorkflowInstanceRecord instance, Guid actorId, string actionKey, string idempotencyKey,
        Guid? fileId, Guid formVersionId, long formRevision, string submissionJson, CancellationToken token)
    {
        if (fileId is null) return Result<bool>.Success(true);
        var originalAssignee = await OriginalAssigneeAsync(todo, instance, token).ConfigureAwait(false);
        if (signature is null || signature.TodoId != todo.Id || signature.StepId != todo.StepId || signature.InstanceId != instance.Id ||
            signature.TenantScopeKey != instance.TenantScopeKey ||
            signature.ActorUserId != actorId || signature.OriginalAssigneeUserId != originalAssignee || signature.ActionKey != actionKey ||
            signature.IdempotencyKey != idempotencyKey.Trim() || signature.FormVersionId != formVersionId ||
            signature.FileId != fileId || signature.FormRevision != formRevision || signature.SubmissionHash != HashSubmission(submissionJson))
            return Result<bool>.Failure(new Error(WorkflowErrorCodes.TodoSignatureInvalid, "The signature does not match this action.", ErrorType.Conflict));
        if (!await IsFormBoundAsync(todo, instance, actionKey, formVersionId, token).ConfigureAwait(false))
            return Result<bool>.Failure(new Error(WorkflowErrorCodes.TodoSignatureInvalid, "The evidence form is not bound to this activity.", ErrorType.Conflict));
        await command.ExecuteAsync(WorkflowSignatureSql.Insert,
            WorkflowSqlParameters.Create(("Id", signature.Id), ("InstanceId", signature.InstanceId), ("StepId", signature.StepId),
                ("TodoId", signature.TodoId), ("TenantScopeKey", signature.TenantScopeKey), ("ActorUserId", signature.ActorUserId),
                ("OriginalAssigneeUserId", signature.OriginalAssigneeUserId), ("ActionKey", signature.ActionKey),
                ("IdempotencyKey", signature.IdempotencyKey), ("FormVersionId", signature.FormVersionId), ("FormRevision", signature.FormRevision),
                ("SubmissionHash", signature.SubmissionHash), ("FileId", signature.FileId), ("FileHash", signature.FileHash), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    /// <summary>代理证据同时固定实际签署人与委托人，不把代理持有人误写为原始授权人。</summary>
    private async Task<Guid> OriginalAssigneeAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance, CancellationToken token)
    {
        var grant = await query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            WorkflowSqlParameters.Create(("TodoId", todo.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return grant?.OriginalAssigneeUserId ?? todo.AssigneeUserId;
    }

    private async Task<bool> IsFormBoundAsync(WorkflowTodoRuntimeRecord todo, WorkflowInstanceRecord instance, string actionKey, Guid formVersionId, CancellationToken token)
    {
        if (formVersionId == Guid.Empty) return false;
        if (actionKey != "vote") return instance.FormVersionId == formVersionId;
        if (todo.NodeTypeKey != WorkflowHumanActivityKinds.Vote) return false;
        var binding = await query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("NodeKey", todo.NodeKey),
                ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return binding is { FormStatusKey: "active" } && binding.BallotFormVersionId == formVersionId;
    }

    public async Task FinishAsync(WorkflowPreparedSignature? signature, CancellationToken token)
    {
        if (signature is null || signature.TenantScopeKey != "host") return;
        var key = HostFileReferenceClaimIdempotencyKeys.WorkflowActionSignature(signature.Id);
        try
        {
            // 幂等重放可能没有消费本次准备结果，必须探测该次证据身份，不能把响应成功等同于新证据提交。
            var exists = await query.QuerySingleOrDefaultAsync<int>(WorkflowSignatureSql.Exists,
                WorkflowSqlParameters.Create(("Id", signature.Id), ("TenantScopeKey", signature.TenantScopeKey)), token).ConfigureAwait(false);
            var succeeded = exists == 1
                ? (await claims.ConfirmAsync(key, token).ConfigureAwait(false)).IsSuccess
                : (await claims.ReleaseAsync(key, token).ConfigureAwait(false)).IsSuccess;
            if (!succeeded) logger.LogWarning("Workflow signature claim remains pending for {EvidenceId}.", signature.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Workflow signature claim awaits reconciliation for {EvidenceId}.", signature.Id);
        }
    }

    private async Task<SignatureDescriptor?> ReadHostDescriptorAsync(Guid id, CancellationToken token)
    {
        var row = await descriptors.GetReadyDescriptorAsync(id, token).ConfigureAwait(false);
        return row is null ? null : new(row.SizeBytes, row.ContentType, row.ContentHash, row.CreatedByUserId);
    }

    private async Task<SignatureDescriptor?> ReadTenantDescriptorAsync(Guid actor, Guid id, CancellationToken token)
    {
        var row = await tenantDescriptors.GetReadyDescriptorAsync(SignatureOwnerModuleKey, actor, id, token).ConfigureAwait(false);
        return row is null ? null : new(row.SizeBytes, row.ContentType, row.ContentHash, row.CreatedByUserId);
    }

    private async Task<Stream?> OpenHostAsync(Guid id, CancellationToken token)
    {
        var result = await contents.OpenReadyContentAsync(id, token).ConfigureAwait(false);
        return result.IsSuccess ? result.Value!.Content : null;
    }

    private async Task<Stream?> OpenTenantAsync(Guid actor, Guid id, CancellationToken token)
    {
        var result = await tenantFiles.OpenReadyContentAsync(SignatureOwnerModuleKey, actor, id, token).ConfigureAwait(false);
        return result.IsSuccess ? result.Value!.Content : null;
    }

    internal static bool HasImageHeader(byte[] bytes, int count, string mime) => mime == "image/png"
        ? count >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        : count >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
    internal static string HashSubmission(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static Result<WorkflowPreparedSignature?> Invalid() => Result<WorkflowPreparedSignature?>.Failure(
        new Error(WorkflowErrorCodes.TodoSignatureInvalid, "The signature file is invalid or unauthorized.", ErrorType.Validation));

    private sealed record SignatureDescriptor(long SizeBytes, string ContentType, string? ContentHash, Guid CreatedByUserId);
}

/// <summary>单次准备的不可变证据绑定，失败或重放只释放自己的预留。</summary>
internal sealed record WorkflowPreparedSignature(Guid Id, Guid InstanceId, Guid StepId, Guid TodoId, string TenantScopeKey,
    Guid ActorUserId, Guid OriginalAssigneeUserId, string ActionKey, string IdempotencyKey, Guid FormVersionId,
    long FormRevision, string SubmissionHash, Guid FileId, string FileHash);
