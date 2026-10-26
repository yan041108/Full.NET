using System.Security.Cryptography;
using Full.NET.Abstractions.Messaging;
using global::MemoryPack;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>业务节点调用只携带根业务定位和固定动作，不传表单、规则快照、脚本或任意SQL。</summary>
[MemoryPackable]
public partial record WorkflowBusinessCommandBinding(Guid CallId, Guid InstanceId, Guid DefinitionVersionId,
    string OwnerKey, string ActionKey, int ActionVersion, string ActionHash, string BusinessType, string BusinessId,
    Guid RequestedByUserId);

/// <summary>执行和补偿共享相同业务输入身份，交付消息与租约身份不参与摘要。</summary>
public static class WorkflowBusinessCommandHash
{
    public static string Compute(WorkflowBusinessCommandBinding binding) =>
        Convert.ToHexStringLower(SHA256.HashData(MemoryPackSerializer.Serialize(binding)));
}

/// <summary>节点业务命令的稳定事件目录，实际所有者由受控OwnerKey与注册Sink确定。</summary>
public static class WorkflowBusinessCommandIntegrationEventTypes
{
    public const string Requested = "fullnet.workflow.business.command.requested";
    public const string Result = "fullnet.workflow.business.command.result";
}

/// <summary>PhaseKey为execute或compensate；租户始终来自消息上下文。</summary>
[MemoryPackable]
public partial record WorkflowBusinessCommandRequestedIntegrationEvent(WorkflowBusinessCommandBinding Binding,
    string InputHash, string PhaseKey, int Generation, DateTimeOffset OccurredAtUtc);

/// <summary>所有者确认的最小回执，只返回效果凭据和错误码，不回传原/新业务快照。</summary>
[MemoryPackable]
public partial record WorkflowBusinessCommandResultIntegrationEvent(Guid CallId, Guid InstanceId, string InputHash,
    string PhaseKey, int Generation, string OutcomeKey, string? OwnerInputHash, long? AppliedVersion, long? CompensatedVersion,
    string? ErrorCode, DateTimeOffset OccurredAtUtc);

/// <summary>真实消费方在自己的事务和场景Port边界内执行，Workflow路由器不持有本地事务。</summary>
public interface IWorkflowBusinessCommandSink
{
    string OwnerKey { get; }
    Task HandleAsync(IntegrationEventContext context, WorkflowBusinessCommandRequestedIntegrationEvent value, CancellationToken cancellationToken);
}

/// <summary>消费方事务外读取的最小权威证明，用于拒绝错根、错输入及未授权补偿。</summary>
public sealed record WorkflowBusinessCommandProof(WorkflowBusinessCommandBinding Binding, string InputHash,
    string RootStatusKey, string CallStatusKey, bool CompensationRequested, int Generation);

/// <summary>只读取当前可信作用域的真实调用，载荷中的租户或业务ID不构成读取授权。</summary>
public interface IWorkflowBusinessCommandProofReader
{
    Task<WorkflowBusinessCommandProof?> FindAsync(Guid callId, Guid instanceId, string inputHash, CancellationToken cancellationToken);
}

/// <summary>实际模块静态注册的动作目录；版本和摘要固定于发布资产，不能从普通请求注册。</summary>
public sealed record WorkflowBusinessActionDefinition(string OwnerKey, string ActionKey, int ActionVersion,
    string ScopeKey, string BusinessType, string PlacementKey);
