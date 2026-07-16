using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Compatibility.AdminNet;

public sealed class AdminNetApiResultMapper : IApiResultMapper
{
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

        var error = result.Error ?? new Error(
            "common.unexpected",
            "An unexpected error occurred.",
            ErrorType.Unexpected);
        return Results.Json(
            new AdminNetEnvelope<T>(
                false,
                error.Code,
                error.Message,
                default,
                traceId),
            statusCode: StandardApiResultMapper.ToStatusCode(error.Type));
    }

    public IResult MapException(Exception exception, HttpContext httpContext) =>
        Map(
            Result<object?>.Failure(new Error(
                "common.unexpected",
                "An unexpected error occurred.",
                ErrorType.Unexpected)),
            httpContext);
}
