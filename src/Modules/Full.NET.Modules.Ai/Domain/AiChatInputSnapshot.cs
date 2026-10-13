using System.Collections;
using System.Text;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>普通聊天预算与派发共享的有界原文快照；不宣称字符或UTF-8字节等于精确Token。</summary>
internal sealed class AiChatInputSnapshot : IReadOnlyList<(string RoleKey, string Content)>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly (string RoleKey, string Content)[] values;
    private AiChatInputSnapshot((string RoleKey, string Content)[] values) { this.values = values; }
    public int Count => values.Length;
    public (string RoleKey, string Content) this[int index] => values[index];
    public IEnumerator<(string RoleKey, string Content)> GetEnumerator() => ((IEnumerable<(string RoleKey, string Content)>)values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>只读取一次外部Count及有限索引；不执行外部枚举、不裁剪正文，不保留外部集合。</summary>
    internal static AiChatInputSnapshot Freeze(IReadOnlyList<(string RoleKey, string Content)> history, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(history);
        // 已拥有的快照不再复制；私有构造函数与私有数组保证预算和派发看到相同原文。
        if (history is AiChatInputSnapshot owned) return owned;
        int count;
        try { count = history.Count; }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException) { throw Invalid(); }
        token.ThrowIfCancellationRequested();
        if (count is < 0 or > AiChatContentPolicy.MaxHistoryMessages) throw Invalid();
        var copy = new (string RoleKey, string Content)[count]; long characters = 0;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); (string RoleKey, string Content) value;
            try { value = history[i]; }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException) { throw Invalid(); }
            token.ThrowIfCancellationRequested();
            if (value.RoleKey is not ("system" or "developer" or "user" or "assistant" or "tool") || value.Content is null) throw Invalid();
            characters += value.Content.Length;
            if (characters > AiChatContentPolicy.MaxHistoryCharacters) throw Invalid();
            try { _ = StrictUtf8.GetByteCount(value.Content); }
            catch (EncoderFallbackException) { throw Invalid(); }
            token.ThrowIfCancellationRequested(); copy[i] = value;
        }
        return new(copy);
    }

    // 不附带底层异常，集合实现不能把来源正文或凭据带进诊断信息。
    private static InvalidDataException Invalid() => new("AI chat history is invalid.");
}
