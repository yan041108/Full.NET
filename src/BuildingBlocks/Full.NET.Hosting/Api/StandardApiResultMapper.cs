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
    public IResult MapException(Exception exception, HttpContext httpContext)
    {
        if (exception is ServiceCapacityExceededException capacityException)
        {
            return MapDatabaseCapacityException(capacityException, httpContext);
        }

        return Map(
            Result<object?>.Failure(UnexpectedError()),
            httpContext);
    }

    private IResult MapDatabaseCapacityException(
        ServiceCapacityExceededException exception,
        HttpContext httpContext)
    {
        var locale = localeContext.CurrentLocale;
        var error = new Error(
            Code: CommonErrorCodes.DatabaseCapacityExhausted,
            Message: "Database capacity is temporarily unavailable.",
            Type: ErrorType.Unexpected);
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = localizer.Localize(error, CultureInfo.GetCultureInfo(locale)),
            Type = $"https://full.net/errors/{error.Code}",
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        var retryAfterSeconds = Math.Max(
            1,
            (int)Math.Ceiling(exception.RetryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(
            CultureInfo.InvariantCulture);
        LocalizationHttpHeaders.Apply(
            httpContext.Response,
            locale,
            varyByAcceptLanguage: true);
        return Results.Problem(problem);
    }

    private IReadOnlyDictionary<string, string[]> LocalizeValidationErrors(
        Error error,
        IReadOnlyList<ValidationViolation> violations,
        CultureInfo culture)
    {
        var violationsByField = violations
            .GroupBy(violation => violation.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
        var fields = (error.ValidationErrors?.Keys ?? [])
            .Concat(violations.Select(violation => violation.Field))
            .Distinct(StringComparer.Ordinal);
        var localizedErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var legacyMessages = error.ValidationErrors is not null
                && error.ValidationErrors.TryGetValue(field, out var messages)
                    ? messages
                    : [];
            var fieldViolations = violationsByField.GetValueOrDefault(field) ?? [];
            var localizedMessages = new List<string>(Math.Max(
                legacyMessages.Length,
                fieldViolations.Length));
            for (var index = 0; index < fieldViolations.Length; index++)
            {
                var defaultMessage = index < legacyMessages.Length
                    ? legacyMessages[index]
                    : error.DefaultMessage;
                var violation = fieldViolations[index];
                localizedMessages.Add(localizer.Localize(
                    new Error(
                        Code: violation.Code,
                        Message: defaultMessage,
                        Type: ErrorType.Validation,
                        ValidationErrors: null,
                        Arguments: violation.Arguments,
                        ValidationViolations: null),
                    culture));
            }

            // 旧生产者可能尚未为每条兼容消息提供结构化违反项；原文必须原序保留，
            // 禁止静默截断，也禁止为了诊断而记录可能包含用户输入的消息内容。
            if (legacyMessages.Length > fieldViolations.Length)
            {
                localizedMessages.AddRange(legacyMessages[fieldViolations.Length..]);
            }

            localizedErrors[field] = localizedMessages.ToArray();
        }

        return localizedErrors;
    }

    private static Error UnexpectedError() => new(
        Code: CommonErrorCodes.Unexpected,
        Message: "An unexpected error occurred.",
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
