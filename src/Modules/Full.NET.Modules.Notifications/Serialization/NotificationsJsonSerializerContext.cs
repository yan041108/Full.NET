using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Serialization;

/// <summary>
/// 为 Notifications 公共 HTTP 契约生成的 System.Text.Json 源生成上下文，
/// 在模块注册时插入 HTTP JSON 选项的解析器链以提升序列化性能。
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostAnnouncementResponse))]
[JsonSerializable(typeof(PagedResult<HostAnnouncementResponse>))]
[JsonSerializable(typeof(CreateHostAnnouncementRequest))]
[JsonSerializable(typeof(UpdateHostAnnouncementRequest))]
[JsonSerializable(typeof(PublishHostAnnouncementRequest))]
[JsonSerializable(typeof(InboxMessageResponse))]
[JsonSerializable(typeof(PagedResult<InboxMessageResponse>))]
[JsonSerializable(typeof(InboxUnreadCountResponse))]
[JsonSerializable(typeof(SendHostInboxMessageRequest))]
[JsonSerializable(typeof(SendTenantInboxMessageRequest))]
[JsonSerializable(typeof(RecipientEndpointResponse))]
[JsonSerializable(typeof(NotificationTemplateResponse))]
[JsonSerializable(typeof(PagedResult<NotificationTemplateResponse>))]
[JsonSerializable(typeof(CreateNotificationTemplateRequest))]
[JsonSerializable(typeof(UpdateNotificationTemplateRequest))]
[JsonSerializable(typeof(PublishNotificationTemplateRequest))]
[JsonSerializable(typeof(NotificationTemplateBody))]
[JsonSerializable(typeof(NotificationTemplateParameterSchema))]
[JsonSerializable(typeof(NotificationTemplateParameterDefinition))]
[JsonSerializable(typeof(CreateNotificationIntentRequest))]
[JsonSerializable(typeof(NotificationIntentResponse))]
[JsonSerializable(typeof(NotificationRecipientInput))]
[JsonSerializable(typeof(NotificationRecipientResponse))]
[JsonSerializable(typeof(NotificationProviderTypeDescriptor))]
[JsonSerializable(typeof(IReadOnlyList<NotificationProviderTypeDescriptor>))]
[JsonSerializable(typeof(NotificationProviderConfigField))]
[JsonSerializable(typeof(IReadOnlyList<NotificationProviderConfigField>))]
[JsonSerializable(typeof(NotificationProviderProfileResponse))]
[JsonSerializable(typeof(PagedResult<NotificationProviderProfileResponse>))]
[JsonSerializable(typeof(CreateNotificationProviderProfileRequest))]
[JsonSerializable(typeof(UpdateNotificationProviderProfileRequest))]
[JsonSerializable(typeof(PublishNotificationProviderProfileRequest))]
[JsonSerializable(typeof(SetNotificationProviderProfileEnabledRequest))]
[JsonSerializable(typeof(NotificationBindingResponse))]
[JsonSerializable(typeof(PagedResult<NotificationBindingResponse>))]
[JsonSerializable(typeof(CreateNotificationBindingRequest))]
[JsonSerializable(typeof(UpdateNotificationBindingRequest))]
[JsonSerializable(typeof(PublishNotificationBindingRequest))]
[JsonSerializable(typeof(NotificationBindingTargetInput))]
[JsonSerializable(typeof(IReadOnlyList<NotificationBindingTargetInput>))]
[JsonSerializable(typeof(IReadOnlyList<NotificationTemplateParameterDefinition>))]
[JsonSerializable(typeof(IReadOnlyList<NotificationRecipientInput>))]
[JsonSerializable(typeof(IReadOnlyList<NotificationRecipientResponse>))]
[JsonSerializable(typeof(NotificationDeliveryResponse))]
[JsonSerializable(typeof(PagedResult<NotificationDeliveryResponse>))]
[JsonSerializable(typeof(NotificationDeliveryAttemptResponse))]
[JsonSerializable(typeof(IReadOnlyList<NotificationDeliveryAttemptResponse>))]
[JsonSerializable(typeof(RetryNotificationDeliveryRequest))]
[JsonSerializable(typeof(NotificationReceiptAcceptedResponse))]
internal partial class NotificationsJsonSerializerContext : JsonSerializerContext;
