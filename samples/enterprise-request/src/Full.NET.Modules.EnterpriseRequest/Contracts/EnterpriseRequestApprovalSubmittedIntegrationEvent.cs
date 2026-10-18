using MemoryPack;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>提交意图已与单据状态原子持久化，后台据此启动固定实例。</summary>
/// <param name="SubmissionId">不可变提交日志标识，租户来自可信事件元数据。</param>
/// <param name="RequestId">对应企业申请标识，必须与提交日志一致。</param>
[MemoryPackable]
public sealed partial record EnterpriseRequestApprovalSubmittedIntegrationEvent(Guid SubmissionId, Guid RequestId)
{
    /// <summary>可靠启动事件的稳定类型。</summary>
    public const string EventType = "fullnet.enterprise_request.approval.submitted";
    /// <summary>载荷模式版本。</summary>
    public const int SchemaVersion = 1;
}
