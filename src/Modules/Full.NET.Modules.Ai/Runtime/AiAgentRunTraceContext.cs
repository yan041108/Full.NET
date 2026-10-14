using System.Diagnostics;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>只保存有限 W3C 追踪标识；不接收业务请求字段，不持久化 Baggage、TraceState 或身份。</summary>
internal static class AiAgentRunTraceContext
{
    internal static string? Capture()
    {
        var current = Activity.Current;
        if (current?.IdFormat != ActivityIdFormat.W3C) return null;
        var context = current.Context;
        if (context.TraceId == default || context.SpanId == default) return null;
        return $"00-{context.TraceId.ToHexString()}-{context.SpanId.ToHexString()}-{((context.TraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00")}";
    }

    /// <summary>旧记录和损坏诊断值均忽略；仅允许本版本写出的 55 字符上下文，不改变业务恢复资格。</summary>
    internal static bool TryRead(string? value, out ActivityContext context)
    {
        context = default;
        if (value is not { Length: 55 } || !value.StartsWith("00-", StringComparison.Ordinal)
            || value[35] != '-' || value[52] != '-' || value[53] != '0' || value[54] is not ('0' or '1')) return false;
        for (var i = 3; i < 52; i++)
            if (i != 35 && value[i] is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return ActivityContext.TryParse(value, null, isRemote: true, out context);
    }
}
