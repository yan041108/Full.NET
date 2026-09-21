using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.DataApproval.Contracts;

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>跨模块提交 DataApproval 请求的命令。</summary>
/// <param name="ScenarioKey">稳定场景键。</param>
/// <param name="TargetEntityId">被变更实体标识。</param>
/// <param name="ProposedChangeJson">提议变更 JSON。</param>
/// <param name="IdempotencyKey">调用方幂等键。</param>
public sealed record SubmitDataApprovalRequestCommand(
    string ScenarioKey,
    Guid TargetEntityId,
    string ProposedChangeJson,
    string IdempotencyKey);

/// <summary>跨模块可见的 DataApproval 提交结果摘要。</summary>
/// <param name="RequestId">审批请求标识。</param>
/// <param name="ScenarioKey">场景键。</param>
/// <param name="StatusKey">当前状态键。</param>
/// <param name="BeforeSnapshotJson">变更前快照 JSON。</param>
/// <param name="AfterSnapshotJson">提议变更 JSON。</param>
/// <param name="WorkflowDefinitionVersionId">固定的工作流定义版本标识。</param>
/// <param name="Version">审批请求乐观并发版本。</param>
public sealed record SubmittedDataApprovalRequest(
    Guid RequestId,
    string ScenarioKey,
    string StatusKey,
    string? BeforeSnapshotJson,
    string AfterSnapshotJson,
    Guid WorkflowDefinitionVersionId,
    long Version);

/// <summary>读取场景是否要求业务模块阻断直接写入。</summary>
public interface IDataApprovalScenarioPolicyPort
{
    /// <summary>判断指定场景在当前作用域已启用且应阻断直接写入。</summary>
    /// <param name="scenarioKey">稳定场景键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> BlocksDirectWriteAsync(
        string scenarioKey,
        CancellationToken cancellationToken = default);
}

/// <summary>供业务模块在本地校验后提交 DataApproval 请求的最小端口。</summary>
public interface IDataApprovalSubmissionPort
{
    /// <summary>创建审批请求并在支持的场景下启动工作流。</summary>
    /// <param name="actorUserId">提交人用户标识。</param>
    /// <param name="command">提交命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<SubmittedDataApprovalRequest>> SubmitAsync(
        Guid actorUserId,
        SubmitDataApprovalRequestCommand command,
        CancellationToken cancellationToken = default);
}
