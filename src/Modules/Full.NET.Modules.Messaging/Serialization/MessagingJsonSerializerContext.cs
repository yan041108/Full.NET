using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChangeDeliveryOwnerRequest))]
[JsonSerializable(typeof(DeadLetterReplayResponse))]
[JsonSerializable(typeof(DeadLetterResponse))]
[JsonSerializable(typeof(DeliveryCutoverResponse))]
[JsonSerializable(typeof(DeliveryRollbackResponse))]
[JsonSerializable(typeof(DeliveryStatusResponse))]
[JsonSerializable(typeof(EventDeliveryOwner))]
[JsonSerializable(typeof(EventStreamStatusResponse))]
[JsonSerializable(typeof(OutboxBacklogSummaryResponse))]
[JsonSerializable(typeof(PagedResult<DeadLetterResponse>))]
[JsonSerializable(typeof(ReplayDeadLetterRequest))]
internal partial class MessagingJsonSerializerContext : JsonSerializerContext;
