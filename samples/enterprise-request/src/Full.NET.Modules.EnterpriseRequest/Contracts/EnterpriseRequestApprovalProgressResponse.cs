using System.Text.Json.Serialization;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>本模块审批权威进度及通知所有者的投递摘要；不代表 Workflow 当前节点或用户已经阅读。</summary>
/// <param name="RequestId">当前有权读取的单据标识。</param>
/// <param name="RequestStatus">单据业务状态。</param>
/// <param name="RequestVersion">当前单据版本。</param>
/// <param name="DeliveryState">本模块提交/启动回执/终态回写阶段。</param>
/// <param name="WorkflowDefinitionVersionId">提交时固定的流程定义版本；未绑定时为空。</param>
/// <param name="WorkflowInstanceId">提交时固定的实例标识；未绑定时为空。</param>
/// <param name="SubmittedVersion">提交日志固定的单据版本。</param>
/// <param name="SubmittedAtUtc">提交记录落库时间；历史补绑定时为恢复记录时间，不推定原提交时间。</param>
/// <param name="StartedAtUtc">已记录的启动回执时间；终态可能先到达。</param>
/// <param name="CompletedAtUtc">终态回写时间。</param>
/// <param name="FinalNotification">终态提醒的所有者摘要；终态已回写但为空时表示尚未查到受理意图，不推定补投次数或发送成功。</param>
public sealed record EnterpriseRequestApprovalProgressResponse(
    Guid RequestId, string RequestStatus,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RequestVersion,
    EnterpriseRequestApprovalDeliveryState DeliveryState,
    Guid? WorkflowDefinitionVersionId, Guid? WorkflowInstanceId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? SubmittedVersion,
    DateTimeOffset? SubmittedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    NotificationIntentDeliverySnapshot? FinalNotification = null);

/// <summary>稳定线协议阶段；后续新增值须同步客户端闭合校验。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnterpriseRequestApprovalDeliveryState>))]
public enum EnterpriseRequestApprovalDeliveryState
{
    /// <summary>草稿尚未提交。</summary>
    [JsonStringEnumMemberName("not_submitted")]
    NotSubmitted,
    /// <summary>提交意图已持久化，启动回执尚未记录。</summary>
    [JsonStringEnumMemberName("queued")]
    Queued,
    /// <summary>已记录流程启动回执，终态尚未回写。</summary>
    [JsonStringEnumMemberName("started")]
    Started,
    /// <summary>已原子回写终态并封存提交日志。</summary>
    [JsonStringEnumMemberName("finalized")]
    Finalized,
    /// <summary>历史非草稿缺少可信流程绑定，需要受控恢复。</summary>
    [JsonStringEnumMemberName("recovery_required")]
    RecoveryRequired
}
