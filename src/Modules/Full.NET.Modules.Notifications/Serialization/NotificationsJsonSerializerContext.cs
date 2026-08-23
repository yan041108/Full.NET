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
internal partial class NotificationsJsonSerializerContext : JsonSerializerContext;
