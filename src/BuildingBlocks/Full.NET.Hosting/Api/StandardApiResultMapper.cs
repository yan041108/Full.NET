using System.Diagnostics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 将应用结果映射为标准成功响应或 RFC ProblemDetails 错误响应。
/// </summary>
/// <param name="localizer">统一错误显示文本本地化器。</param>
/// <param name="localeContext">当前请求已经规范化的语言上下文。</param>
public sealed class StandardApiResultMapper(
    IErrorMessageLocalizer localizer,
    ILocaleContext localeContext) : IApiResultMapper
{
    /// <inheritdoc />
    public IResult Map<T>(Result<T> result, HttpContext httpContext)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var error = result.Error ?? UnexpectedError();
        var locale = localeContext.CurrentLocale;
        var culture = CultureInfo.GetCultureInfo(locale);
        var localizedMessage = localizer.Localize(error, culture);
        var problem = new ProblemDetails
        {
            Status = ToStatusCode(error.Type),
            Title = localizedMessage,
            Type = $"https://full.net/errors/{error.Code}"
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        if (error.ValidationViolations is { Count: > 0 } violations)
        {
            problem.Extensions["violations"] = violations;
            problem.Extensions["errors"] = LocalizeValidationErrors(
                error,
                violations,
                culture);
        }
        else if (error.ValidationErrors is not null)
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        LocalizationHttpHeaders.Apply(
            httpContext.Response,
            locale,
            varyByAcceptLanguage: true);
        return Results.Problem(problem);
    }

    /// <inheritdoc />
    public IResult MapException(Exception exception, HttpContext httpContext) =>
        Map(
            Result<object?>.Failure(UnexpectedError()),
            httpContext);

    private IReadOnlyDictionary<string, string[]> LocalizeValidationErrors(
        Error error,
        IReadOnlyList<ValidationViolation> violations,
        CultureInfo culture)
    {
        var messageIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        return violations
            .GroupBy(violation => violation.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(violation =>
                {
                    var index = messageIndexes.GetValueOrDefault(group.Key);
                    messageIndexes[group.Key] = index + 1;
                    var defaultMessage = error.ValidationErrors is not null
                        && error.ValidationErrors.TryGetValue(group.Key, out var messages)
                        && index < messages.Length
                            ? messages[index]
                            : error.DefaultMessage;
                    return localizer.Localize(
                        new Error(
                            Code: violation.Code,
                            DefaultMessage: defaultMessage,
                            Type: ErrorType.Validation,
                            Arguments: violation.Arguments),
                        culture);
                }).ToArray(),
                StringComparer.Ordinal);
    }

    private static Error UnexpectedError() => new(
        Code: CommonErrorCodes.Unexpected,
        DefaultMessage: "An unexpected error occurred.",
        Type: ErrorType.Unexpected);

    /// <summary>
    /// 将稳定错误类型映射到标准 HTTP 状态码。
    /// </summary>
    /// <param name="type">应用层错误类型。</param>
    /// <returns>对应的标准 HTTP 状态码。</returns>
    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError
    };
}
