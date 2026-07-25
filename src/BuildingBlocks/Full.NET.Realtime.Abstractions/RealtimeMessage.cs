namespace Full.NET.Realtime;

/// <summary>
/// 实时下行消息信封：稳定机器码 + 可选结构化数据，禁止依赖翻译文本。
/// </summary>
public sealed record RealtimeMessage(
    string Code,
    IReadOnlyDictionary<string, object?>? Data = null);
