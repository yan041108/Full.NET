using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>为运维提供显式状态与恢复入口；不新建 Apply，也不根据请求重算生成内容。</summary>
internal sealed class CodeGenerationWorkspaceService(ICommandExecutor commands, IQueryExecutor queries,
    IOptions<CodeGenerationApplyOptions> options, CodeGenerationApplyGate gate, CodeGenerationGitWorkspaceService git, IClock clock, CodeGenerationArtifactStore archives)
{
    /// <summary>在完整文件租约内读取顺序和阶段；占用时返回 Busy，不根据超时推断旧执行者已经停止。</summary>
    public Task<Result<CodeGenerationWorkspaceStateResponse>> GetAsync(CancellationToken token = default) => ExecuteAsync(null, token);

    /// <summary>恢复指定的既有 Host 操作；Endpoint 必须同时要求 Apply 与 Rollback 权限。</summary>
    public Task<Result<CodeGenerationWorkspaceStateResponse>> RecoverAsync(CodeGenerationWorkspaceRecoveryRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.OperationId == Guid.Empty ? Task.FromResult(Failure(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, "A pending operation identifier is required."))
            : ExecuteAsync(request.OperationId, token);
    }

    private async Task<Result<CodeGenerationWorkspaceStateResponse>> ExecuteAsync(Guid? recoveryId, CancellationToken token)
    {
        if (!options.Value.Enabled || !Directory.Exists(options.Value.WorkspaceRoot))
            return Failure(CodeGenerationRunErrorCodes.ApplyDisabled, "The configured workspace is unavailable or disabled.");
        try
        {
            if (!await gate.TryEnterAsync(token).ConfigureAwait(false))
                return Failure(CodeGenerationRunErrorCodes.ApplyBusy, "Another workspace operation is in progress.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return Failure(CodeGenerationRunErrorCodes.RecoveryRequired, "The persistent workspace identity requires review."); }
        try
        {
            var pending = await GenerationWorkspaceJournal.ReadPendingAsync(gate.Lease, token).ConfigureAwait(false);
            if (recoveryId.HasValue && pending is null)
            {
                var completed = await GenerationWorkspaceJournal.ReadOperationAsync(gate.Lease, recoveryId.Value, token).ConfigureAwait(false);
                if (completed is not { Client: "host", Phase: "completed" })
                    return Failure(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, "The selected Host operation has no recoverable or completed receipt.");
            }
            if (recoveryId.HasValue && pending is not null)
            {
                if (pending.Id != recoveryId || pending.Client != "host")
                    return Failure(CodeGenerationRunErrorCodes.InvalidRecoveryOperation, "Recovery must select the pending Host operation; CLI operations require the controlled CLI.");
                await CodeGenerationWorkspaceRecovery.RecoverAsync(gate.Lease, options.Value.WorkspaceRoot, commands, queries, git, clock, archives, token).ConfigureAwait(false);
                pending = await GenerationWorkspaceJournal.ReadPendingAsync(gate.Lease, token).ConfigureAwait(false);
            }
            var head = await GenerationWorkspaceJournal.ReadHeadAsync(gate.Lease, token).ConfigureAwait(false);
            return Result<CodeGenerationWorkspaceStateResponse>.Success(new(gate.WorkspaceId, head.Revision, head.TipOperationId, head.LastOperationId,
                pending?.Id, pending?.Kind, pending?.Client, pending?.Phase ?? "idle"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return Failure(CodeGenerationRunErrorCodes.RecoveryRequired, "The saved workspace evidence requires review before recovery can continue."); }
        finally { await gate.ReleaseAsync().ConfigureAwait(false); }
    }

    private static Result<CodeGenerationWorkspaceStateResponse> Failure(string code, string message) =>
        Result<CodeGenerationWorkspaceStateResponse>.Failure(new Error(code, message, ErrorType.Conflict));
}
