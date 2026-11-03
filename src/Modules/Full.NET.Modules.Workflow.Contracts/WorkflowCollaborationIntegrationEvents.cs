using global::MemoryPack;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>协作协议的源请求目录；消息信封保留真实源租户，不把目标租户放入当前上下文。</summary>
public static class WorkflowCollaborationIntegrationEventTypes
{
    /// <summary>源调用及固定投影已与事务Outbox共同提交。</summary>
    public const string Requested = "fullnet.workflow.collaboration.requested";
    /// <summary>目标的唯一真实根已创建，源须认证收件后才能确认。</summary>
    public const string Created = "fullnet.workflow.collaboration.created";
    /// <summary>目标及其后代已经取得可证明的终态，最小结果等待源接纳。</summary>
    public const string Completed = "fullnet.workflow.collaboration.completed";
    /// <summary>关闭意图已按实际目标终态收敛，不把未知副作用伪装为取消成功。</summary>
    public const string Cancelled = "fullnet.workflow.collaboration.cancelled";
}

/// <summary>消息只引用已持久化的请求证明；摘要不是签名，消费者还必须核对信封与本地事实。</summary>
[MemoryPackable]
public partial record WorkflowCollaborationRequestedIntegrationEvent(Guid CallId, Guid DeliveryId, string BindingHash);

/// <summary>三种目标协议消息共用最小引用结构；种类由静态事件目录与耐久正文共同核对。</summary>
[MemoryPackable]
public partial record WorkflowCollaborationControlIntegrationEvent(Guid CallId, Guid ControlId, string BindingHash, string BodyHash);
