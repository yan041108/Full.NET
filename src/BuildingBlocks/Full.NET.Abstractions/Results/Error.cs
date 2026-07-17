using System.Text.Json.Serialization;

namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示跨应用层与传输层传递的稳定错误契约。
/// </summary>
/// <remarks>
/// <see cref="Code"/>、<see cref="Type"/>、<see cref="Arguments"/> 和
/// <see cref="ValidationViolations"/> 属于机器契约，不得随显示语言改变；
/// <see cref="DefaultMessage"/> 只用于资源缺失或格式化失败时的安全回退。
/// </remarks>
public sealed record Error
{
    /// <summary>
    /// 使用既有四参数公共契约初始化错误。
    /// </summary>
    /// <param name="Code">稳定错误码。</param>
    /// <param name="Message">兼容既有调用方的安全默认消息。</param>
    /// <param name="Type">稳定错误类型。</param>
    /// <param name="ValidationErrors">兼容既有客户端的字段消息。</param>
    [JsonConstructor]
    public Error(
        string Code,
        string Message,
        ErrorType Type,
        IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
    {
        this.Code = Code;
        this.Message = Message;
        this.Type = Type;
        this.ValidationErrors = ValidationErrors;
    }

    /// <summary>
    /// 使用结构化参数与验证违反项初始化扩展错误契约。
    /// </summary>
    /// <remarks>
    /// 扩展参数位于既有四参数之后，避免三参数调用或第四参数为
    /// <see langword="null"/> 时产生重载歧义。
    /// </remarks>
    /// <param name="Code">稳定错误码。</param>
    /// <param name="Message">兼容既有调用方的安全默认消息。</param>
    /// <param name="Type">稳定错误类型。</param>
    /// <param name="ValidationErrors">兼容既有客户端的字段消息。</param>
    /// <param name="Arguments">安全的命名格式化参数。</param>
    /// <param name="ValidationViolations">结构化字段验证违反项。</param>
    public Error(
        string Code,
        string Message,
        ErrorType Type,
        IReadOnlyDictionary<string, string[]>? ValidationErrors,
        IReadOnlyDictionary<string, object?>? Arguments,
        IReadOnlyList<ValidationViolation>? ValidationViolations)
        : this(Code, Message, Type, ValidationErrors)
    {
        this.Arguments = Arguments;
        this.ValidationViolations = ValidationViolations;
    }

    /// <summary>
    /// 获取稳定错误码。
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// 获取或初始化兼容既有调用方与 JSON 合约的安全默认消息。
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// 获取稳定错误类型。
    /// </summary>
    public ErrorType Type { get; init; }

    /// <summary>
    /// 获取兼容既有客户端的字段消息。
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>
    /// 获取安全的命名格式化参数。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Arguments { get; init; }

    /// <summary>
    /// 获取结构化字段验证违反项。
    /// </summary>
    public IReadOnlyList<ValidationViolation>? ValidationViolations { get; init; }

    /// <summary>
    /// 获取明确表达安全回退语义的默认消息别名。
    /// </summary>
    /// <remarks>
    /// 该别名不参与 JSON，避免同时输出 <c>message</c> 与
    /// <c>defaultMessage</c>；公共序列化契约继续使用 <see cref="Message"/>。
    /// </remarks>
    [JsonIgnore]
    public string DefaultMessage => Message;

    /// <summary>
    /// 按既有四元契约解构错误。
    /// </summary>
    /// <param name="Code">稳定错误码。</param>
    /// <param name="Message">安全默认消息。</param>
    /// <param name="Type">稳定错误类型。</param>
    /// <param name="ValidationErrors">兼容字段消息。</param>
    public void Deconstruct(
        out string Code,
        out string Message,
        out ErrorType Type,
        out IReadOnlyDictionary<string, string[]>? ValidationErrors)
    {
        Code = this.Code;
        Message = this.Message;
        Type = this.Type;
        ValidationErrors = this.ValidationErrors;
    }
}
