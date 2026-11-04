using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>人工指定实例与已读版本，恢复原因随本地事务保存；不接受租户、操作者或目标状态。</summary>
/// <param name="WorkflowInstanceId">明确核对的原实例标识。</param>
/// <param name="ExpectedVersion">操作者已读取的申请版本。</param>
/// <param name="Reason">保存到恢复记录的人工原因。</param>
public sealed record RepairEnterpriseRequestApprovalRequest(Guid WorkflowInstanceId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedVersion,
    string Reason);
