using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析并验证抄送节点的闭合收件人配置。</summary>
internal static class WorkflowCcNodeConfiguration
{
    private static readonly HashSet<string> AllowedProperties =
        new(["nextNodeKeys", "nodeName", "recipientUserIds"], StringComparer.Ordinal);

    /// <summary>读取抄送收件人，并拒绝未批准字段、空标识、重复标识和超限集合。</summary>
    /// <param name="config">节点配置 JSON。</param>
    /// <param name="recipientUserIds">验证成功后的稳定用户标识。</param>
    /// <returns>配置满足闭合约束时返回 <see langword="true"/>。</returns>
    public static bool TryReadRecipients(
        JsonElement config,
        out IReadOnlyList<Guid> recipientUserIds)
    {
        recipientUserIds = [];
        if (config.ValueKind != JsonValueKind.Object ||
            config.EnumerateObject().Any(property => !AllowedProperties.Contains(property.Name)) ||
            !config.TryGetProperty("recipientUserIds", out var recipients) ||
            recipients.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = recipients.EnumerateArray().ToArray();
        if (values.Length is < 1 or > 20)
        {
            return false;
        }

        var parsed = new List<Guid>(values.Length);
        var unique = new HashSet<Guid>();
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(value.GetString(), "D", out var userId) ||
                userId == Guid.Empty ||
                !unique.Add(userId))
            {
                return false;
            }

            parsed.Add(userId);
        }

        recipientUserIds = parsed;
        return true;
    }
}
