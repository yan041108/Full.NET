using FluentValidation;
using FluentValidation.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;

namespace Full.NET.Validation.FluentValidation;

/// <summary>
/// 在 CQRS 管道中作为前置校验行为，于 Handler 与事务执行前短路返回校验失败。
/// </summary>
/// <typeparam name="TMessage">被校验的消息类型（Command 或 Query）。</typeparam>
/// <typeparam name="TResult">Handler 最终返回的结果类型。</typeparam>
/// <remarks>
/// <para>本行为在 <c>next</c> 调用前依次执行所有注册到 <c>IValidator&lt;TMessage&gt;</c> 的规则；
/// 任一规则产生非空 <c>ErrorMessage</c> 即视为校验失败，立即返回 <see cref="ValidationErrorCodes.Failed"/>，
/// 不会进入后续管道或开启业务事务，从而保证无效输入不会触发持久化副作用。</para>
/// <para>错误码非 <see cref="ValidationErrorCodes.Prefix"/> 前缀的失败会被归一为
/// <see cref="ValidationErrorCodes.InvalidFormat"/>，避免未受控的 FluentValidation 默认错误码进入稳定契约；
/// 字段错误按字段名分组、按 (Field, Code, DefaultMessage) 去重后生成
/// <see cref="ValidationViolation"/> 数组供前端按字段渲染。</para>
/// <para>允许透出的参数仅限数值型边界（MinLength/MaxLength/From/To），其他占位符不进入对外契约，
/// 防止把内部计算值或敏感上下文泄露到客户端。</para>
/// </remarks>
internal sealed class FluentValidationBehavior<TMessage, TResult>(
    IEnumerable<IValidator<TMessage>> validators)
    : IDispatchBehavior<TMessage, TResult>
{
    /// <summary>
    /// 执行所有注册校验器；存在失败时短路返回校验错误，否则委托 <paramref name="next"/> 进入后续管道。
    /// </summary>
    /// <param name="message">被校验的消息实例，包含输入字段与业务上下文。</param>
    /// <param name="next">后续管道或 Handler 委托；仅在校验全部通过时被调用。</param>
    /// <param name="cancellationToken">用于取消校验或后续管道的令牌。</param>
    /// <returns>校验通过时返回 Handler 结果；失败时返回承载字段级错误结构的 <see cref="Result{TResult}.Failure"/>。</returns>
    public async Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(
                new ValidationContext<TMessage>(message),
                cancellationToken);
            failures.AddRange(result.Errors.Where(failure =>
                !string.IsNullOrWhiteSpace(failure.ErrorMessage)));
        }

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var normalizedFailures = failures
            .Select(failure => new NormalizedFailure(
                failure.PropertyName,
                NormalizeErrorCode(failure.ErrorCode),
                failure.ErrorMessage,
                ExtractArguments(failure)))
            .DistinctBy(
                failure => new
                {
                    failure.Field,
                    failure.Code,
                    failure.DefaultMessage,
                })
            .ToArray();

        var errors = normalizedFailures
            .GroupBy(failure => failure.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.DefaultMessage)
                    .ToArray(),
                StringComparer.Ordinal);
        var violations = normalizedFailures
            .Select(failure => new ValidationViolation(
                failure.Field,
                failure.Code,
                failure.Arguments))
            .ToArray();

        return Result<TResult>.Failure(new Error(
            Code: ValidationErrorCodes.Failed,
            Message: "One or more validation errors occurred.",
            Type: ErrorType.Validation,
            ValidationErrors: errors,
            Arguments: null,
            ValidationViolations: violations));
    }

    private static string NormalizeErrorCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.StartsWith(ValidationErrorCodes.Prefix, StringComparison.Ordinal)
            ? code
            : ValidationErrorCodes.InvalidFormat;

    private static IReadOnlyDictionary<string, object?> ExtractArguments(
        ValidationFailure failure)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyAllowedArgument(failure, arguments, "MinLength");
        CopyAllowedArgument(failure, arguments, "MaxLength");
        CopyAllowedArgument(failure, arguments, "From");
        CopyAllowedArgument(failure, arguments, "To");
        return arguments;
    }

    private static void CopyAllowedArgument(
        ValidationFailure failure,
        IDictionary<string, object?> destination,
        string name)
    {
        if (failure.FormattedMessagePlaceholderValues is not null
            && failure.FormattedMessagePlaceholderValues.TryGetValue(name, out var value)
            && value is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal)
        {
            destination[name] = value;
        }
    }

    private sealed record NormalizedFailure(
        string Field,
        string Code,
        string DefaultMessage,
        IReadOnlyDictionary<string, object?> Arguments);
}
