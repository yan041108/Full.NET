namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示一个可由客户端依据稳定代码重新渲染的字段验证违反项。
/// </summary>
/// <param name="Field">稳定字段路径，不随语言变化。</param>
/// <param name="Code">稳定验证错误码，不随语言变化。</param>
/// <param name="Arguments">仅包含允许公开且用于显示格式化的命名参数。</param>
public sealed record ValidationViolation(
    string Field,
    string Code,
    IReadOnlyDictionary<string, object?> Arguments);
