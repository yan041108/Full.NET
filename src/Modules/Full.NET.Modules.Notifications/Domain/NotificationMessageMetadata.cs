using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>不可变业务元数据；深链只能引用闭合资源，不能携带任意网址或审批指令。</summary>
internal static class NotificationMessageMetadata
{
    public static bool IsValid(NotificationDeepLink? link) => link is null
        || (link.ResourceId != Guid.Empty && link.ResourceTypeKey is "workflow_todo" or "workflow_instance");

    public static string Freeze(string routeJson, DateTimeOffset? expiresAtUtc, NotificationDeepLink? link, long policyVersion)
    {
        var root = new JsonObject { ["targets"] = JsonNode.Parse(routeJson)!.AsArray() };
        root["policyVersion"] = policyVersion;
        if (expiresAtUtc is { } expiry) root["expiresAtUtc"] = expiry.ToUniversalTime().ToString("O");
        if (link is not null) root["deepLink"] = new JsonObject
        {
            ["resourceTypeKey"] = link.ResourceTypeKey, ["resourceId"] = link.ResourceId.ToString("D"),
        };
        return root.ToJsonString();
    }

    public static (DateTimeOffset? ExpiresAtUtc, NotificationDeepLink? DeepLink) Read(string routeJson)
    {
        using var document = JsonDocument.Parse(routeJson);
        var root = document.RootElement;
        DateTimeOffset? expiry = root.TryGetProperty("expiresAtUtc", out var value) ? value.GetDateTimeOffset() : null;
        NotificationDeepLink? link = null;
        if (root.TryGetProperty("deepLink", out var item))
            link = new NotificationDeepLink(item.GetProperty("resourceTypeKey").GetString()!, item.GetProperty("resourceId").GetGuid());
        return (expiry, IsValid(link) ? link : null);
    }
}
