namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示跨应用层与传输层传递的稳定错误契约。
/// </summary>
/// <remarks>
/// <see cref="Code"/>、<see cref="Type"/>、<see cref="Arguments"/> 和
/// <see cref="ValidationViolations"/> 属于机器契约，不得随显示语言改变；
/// <see cref="DefaultMessage"/> 只用于资源缺失或格式化失败时的安全回退。
/// </remarks>
public sealed record Error(
    string Code,
    string DefaultMessage,
    ErrorType Type,
    IReadOnlyDictionary<string, object?>? Arguments = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    IReadOnlyList<ValidationViolation>? ValidationViolations = null)
{
    /// <summary>
    /// 获取兼容既有调用方的安全默认消息。
    /// </summary>
    /// <remarks>
    /// 新代码应使用 <see cref="DefaultMessage"/> 明确其回退语义；该别名不表示文本已经本地化。
    /// </remarks>
    public string Message => DefaultMessage;
}
