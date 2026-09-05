using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>审批待办的不可变超时、催办与升级策略。</summary>
/// <param name="DueAfterMinutes">待办到达后进入逾期的分钟数。</param>
/// <param name="ReminderIntervalMinutes">逾期后两次催办之间的分钟数。</param>
/// <param name="MaxReminderCount">升级前允许发送的催办次数。</param>
/// <param name="EscalationAfterMinutes">待办到达后发送升级通知的分钟数。</param>
/// <param name="EscalationRecipientUserId">固定的升级通知接收人。</param>
internal sealed record WorkflowTodoTimeoutPolicy(
    int DueAfterMinutes,
    int ReminderIntervalMinutes,
    int MaxReminderCount,
    int? EscalationAfterMinutes,
    Guid? EscalationRecipientUserId)
{
    private static readonly HashSet<string> AllowedProperties =
    [
        "dueAfterMinutes",
        "reminderIntervalMinutes",
        "maxReminderCount",
        "escalationAfterMinutes",
        "escalationRecipientUserId",
    ];

    /// <summary>从审批节点配置读取闭合策略；未配置策略时返回成功且策略为空。</summary>
    /// <param name="config">审批节点配置。</param>
    /// <param name="policy">解析后的不可变策略。</param>
    /// <returns>配置是否满足闭合结构和数值边界。</returns>
    public static bool TryRead(JsonElement config, out WorkflowTodoTimeoutPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!config.TryGetProperty("timeoutPolicy", out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().Any(item => !AllowedProperties.Contains(item.Name)) ||
            !TryReadInt(value, "dueAfterMinutes", 1, 525_600, out var due) ||
            !TryReadInt(value, "reminderIntervalMinutes", 1, 43_200, out var interval) ||
            !TryReadInt(value, "maxReminderCount", 0, 100, out var maxReminders))
        {
            return false;
        }

        var hasEscalationMinutes = value.TryGetProperty("escalationAfterMinutes", out var escalationMinutes);
        var hasEscalationRecipient = value.TryGetProperty("escalationRecipientUserId", out var escalationRecipient);
        if (hasEscalationMinutes != hasEscalationRecipient)
        {
            return false;
        }

        int? escalationAfter = null;
        Guid? recipientId = null;
        if (hasEscalationMinutes)
        {
            if (escalationMinutes.ValueKind != JsonValueKind.Number ||
                !escalationMinutes.TryGetInt32(out var minutes) ||
                minutes < due || minutes > 525_600 ||
                escalationRecipient.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(escalationRecipient.GetString(), "D", out var parsedRecipient) ||
                parsedRecipient == Guid.Empty)
            {
                return false;
            }

            escalationAfter = minutes;
            recipientId = parsedRecipient;
        }

        if (maxReminders == 0 && !hasEscalationMinutes)
        {
            return false;
        }

        policy = new(due, interval, maxReminders, escalationAfter, recipientId);
        return true;
    }

    /// <summary>读取必需整数并校验闭合范围。</summary>
    /// <param name="value">策略 JSON 对象。</param>
    /// <param name="name">属性名称。</param>
    /// <param name="minimum">最小允许值。</param>
    /// <param name="maximum">最大允许值。</param>
    /// <param name="result">读取到的整数。</param>
    /// <returns>属性是否为范围内整数。</returns>
    private static bool TryReadInt(
        JsonElement value,
        string name,
        int minimum,
        int maximum,
        out int result)
    {
        result = 0;
        return value.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out result) &&
               result >= minimum && result <= maximum;
    }
}
