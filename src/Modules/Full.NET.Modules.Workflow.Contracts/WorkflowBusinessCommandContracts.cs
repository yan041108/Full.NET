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
    /// <summary>计算业务命令绑定的稳定摘要，用于执行与补偿的输入身份比对。</summary>
    /// <param name="binding">业务命令绑定；摘要只覆盖其序列化内容。</param>
    /// <returns>绑定内容的 SHA-256 十六进制小写摘要。</returns>
    public static string Compute(WorkflowBusinessCommandBinding binding) =>
        Convert.ToHexStringLower(SHA256.HashData(MemoryPackSerializer.Serialize(binding)));
}

/// <summary>节点业务命令的稳定事件目录，实际所有者由受控OwnerKey与注册Sink确定。</summary>
public static class WorkflowBusinessCommandIntegrationEventTypes
{
    /// <summary>业务命令执行请求事件类型；由受控 OwnerKey 与注册 Sink 决定实际所有者。</summary>
    public const string Requested = "fullnet.workflow.business.command.requested";
    /// <summary>所有者回执的结果事件类型；只携带效果凭据与错误码。</summary>
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
    /// <summary>实现方的稳定所有者机器码；路由器据此将绑定匹配到唯一 Sink。</summary>
    string OwnerKey { get; }
    /// <summary>在自己的事务与场景 Port 边界内处理业务命令请求；不得跨模块写表或依赖路由器事务。</summary>
    /// <param name="context">集成事件上下文，提供可信租户、追踪与发生时间等元数据。</param>
    /// <param name="value">业务命令请求事件；执行与补偿共享同一业务输入身份。</param>
    /// <param name="cancellationToken">用于取消处理的令牌。</param>
    Task HandleAsync(IntegrationEventContext context, WorkflowBusinessCommandRequestedIntegrationEvent value, CancellationToken cancellationToken);
}

/// <summary>消费方事务外读取的最小权威证明，用于拒绝错根、错输入及未授权补偿。</summary>
public sealed record WorkflowBusinessCommandProof(WorkflowBusinessCommandBinding Binding, string InputHash,
    string RootStatusKey, string CallStatusKey, bool CompensationRequested, int Generation);

/// <summary>只读取当前可信作用域的真实调用，载荷中的租户或业务ID不构成读取授权。</summary>
public interface IWorkflowBusinessCommandProofReader
{
    /// <summary>读取当前可信作用域内的权威调用证明；用于拒绝错根、错输入及未授权补偿。</summary>
    /// <param name="callId">业务调用标识。</param>
    /// <param name="instanceId">调用所属工作流实例标识。</param>
    /// <param name="inputHash">业务输入身份摘要，用于与持久证明比对。</param>
    /// <param name="cancellationToken">用于取消读取的令牌。</param>
    /// <returns>匹配的调用证明；作用域内不存在时返回 <see langword="null"/>。</returns>
    Task<WorkflowBusinessCommandProof?> FindAsync(Guid callId, Guid instanceId, string inputHash, CancellationToken cancellationToken);
}

/// <summary>实际模块静态注册的动作目录；版本和摘要固定于发布资产，不能从普通请求注册。</summary>
public sealed record WorkflowBusinessActionDefinition(string OwnerKey, string ActionKey, int ActionVersion,
    string ScopeKey, string BusinessType, string PlacementKey);
