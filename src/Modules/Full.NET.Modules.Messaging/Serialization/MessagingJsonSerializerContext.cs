// Messaging 模块 JSON 源生成上下文；Native AOT 绑定契约类型，避免反射序列化。
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ManageOperationalDeadLetters;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Features.ReplayKafkaRange;
using Full.NET.Modules.Messaging.Features.ManageShadowCapture;

namespace Full.NET.Modules.Messaging.Serialization;

/// <summary>
/// 为 Messaging 运维 API 契约与审计差异摘要生成的 System.Text.Json 源生成上下文，
/// 在模块注册时插入 HTTP JSON 选项的解析器链以提升序列化性能，并供审计 Diff 摘要序列化使用。
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StartShadowCaptureRequest))]
[JsonSerializable(typeof(CloseShadowCaptureRequest))]
[JsonSerializable(typeof(ShadowCaptureResponse))]
[JsonSerializable(typeof(ShadowCapturePolicyResponse))]
[JsonSerializable(typeof(ShadowCaptureAuditDiff))]
[JsonSerializable(typeof(CreateKafkaReplayOperationRequest))]
[JsonSerializable(typeof(ChangeKafkaReplayOperationRequest))]
[JsonSerializable(typeof(KafkaReplayOperationResponse))]
[JsonSerializable(typeof(PagedResult<KafkaReplayOperationResponse>))]
[JsonSerializable(typeof(KafkaReplaySnapshot))]
[JsonSerializable(typeof(KafkaReplayOperationAuditDiff))]
[JsonSerializable(typeof(OperationalDeadLetterAuditDiff))]
[JsonSerializable(typeof(OperationalDeadLetterResponse))]
[JsonSerializable(typeof(PagedResult<OperationalDeadLetterResponse>))]
[JsonSerializable(typeof(RecoverDeadLetterRequest))]
[JsonSerializable(typeof(RecoverDeadLetterResponse))]
[JsonSerializable(typeof(DeadLetterReplayAuditDiff))]
[JsonSerializable(typeof(DeliveryCutoverAuditDiff))]
[JsonSerializable(typeof(DeliveryRollbackAuditDiff))]
[JsonSerializable(typeof(RollbackRecoveryAuditDiff))]
[JsonSerializable(typeof(RecoverRollbackRequest))]
[JsonSerializable(typeof(RollbackOperationResponse))]
[JsonSerializable(typeof(VerifyCutoverRequest))]
[JsonSerializable(typeof(CutoverEvidenceResponse))]
[JsonSerializable(typeof(KafkaRangeReplayCancelledAuditDiff))]
[JsonSerializable(typeof(KafkaRangeReplayFailedAuditDiff))]
[JsonSerializable(typeof(KafkaRangeReplayRequestedAuditDiff))]
[JsonSerializable(typeof(KafkaRangeReplaySuccessAuditDiff))]
[JsonSerializable(typeof(ChangeDeliveryOwnerRequest))]
[JsonSerializable(typeof(DeadLetterReplayResponse))]
[JsonSerializable(typeof(DeadLetterResponse))]
[JsonSerializable(typeof(DeliveryCutoverResponse))]
[JsonSerializable(typeof(DeliveryRollbackResponse))]
[JsonSerializable(typeof(DeliveryStatusResponse))]
[JsonSerializable(typeof(EventDeliveryOwner))]
[JsonSerializable(typeof(EventStreamStatusResponse))]
[JsonSerializable(typeof(KafkaRangeReplayRequest))]
[JsonSerializable(typeof(KafkaRangeReplayResponse))]
[JsonSerializable(typeof(OutboxBacklogSummaryResponse))]
[JsonSerializable(typeof(PagedResult<DeadLetterResponse>))]
[JsonSerializable(typeof(ReplayDeadLetterRequest))]
/// <summary>Messaging 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class MessagingJsonSerializerContext : JsonSerializerContext;
