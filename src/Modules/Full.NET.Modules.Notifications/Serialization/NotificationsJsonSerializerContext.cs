using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Serialization;

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
