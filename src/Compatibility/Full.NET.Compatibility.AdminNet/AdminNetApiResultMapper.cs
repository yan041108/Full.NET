using System.Diagnostics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Compatibility.AdminNet;

/// <summary>
/// 在保持真实 HTTP 状态码的前提下映射 Admin.NET 兼容包络。
/// </summary>
/// <param name="localizer">与标准 API 共用的错误显示文本本地化器。</param>
/// <param name="localeContext">当前请求已经规范化的语言上下文。</param>
public sealed class AdminNetApiResultMapper(
    IErrorMessageLocalizer localizer,
    ILocaleContext localeContext,
    IPreV1LegacyErrorCodeProfile legacyErrorCodeProfile) : IApiResultMapper
{
    /// <inheritdoc />
    public IResult Map<T>(Result<T> result, HttpContext httpContext)
    {
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;
        if (result.IsSuccess)
        {
            return Results.Json(
                new AdminNetEnvelope<T>(
                    true,
                    "success",
                    null,
                    result.Value,
                    traceId),
                statusCode: StatusCodes.Status200OK);
        }

        var error = result.Error ?? UnexpectedError();
        var locale = localeContext.CurrentLocale;
        var localizedMessage = localizer.Localize(
            error,
            CultureInfo.GetCultureInfo(locale));
        LocalizationHttpHeaders.Apply(
            httpContext.Response,
            locale,
            varyByAcceptLanguage: true);
        var envelopeCode = legacyErrorCodeProfile.EmitLegacyErrorCodes
            ? PreV1ProtocolCompatibility.ToLegacyErrorCode(error.Code)
            : error.Code;
        return Results.Json(
            new AdminNetEnvelope<T>(
                false,
                envelopeCode,
                localizedMessage,
                default,
                traceId),
            statusCode: StandardApiResultMapper.ToStatusCode(error.Type));
    }

    /// <inheritdoc />
    public IResult MapException(Exception exception, HttpContext httpContext) =>
        Map(
            Result<object?>.Failure(UnexpectedError()),
            httpContext);

    private static Error UnexpectedError() => new(
        Code: CommonErrorCodes.Unexpected,
        Message: "An unexpected error occurred.",
        Type: ErrorType.Unexpected);
}
