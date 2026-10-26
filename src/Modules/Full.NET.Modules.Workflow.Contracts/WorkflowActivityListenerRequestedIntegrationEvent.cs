using global::MemoryPack;

namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>活动最终收敛后请求执行固定版本通知动作；只携带定位信息，不携带表单、签名或来源脚本。</summary>
[MemoryPackable]
public partial record WorkflowActivityListenerRequestedIntegrationEvent(Guid InstanceId, Guid StepId, string NodeKey,
    string ListenerKey, string EventKey, string ActionKey, int ActionVersion, Guid RecipientUserId,
    string BusinessType, string BusinessId, DateTimeOffset OccurredAtUtc);
