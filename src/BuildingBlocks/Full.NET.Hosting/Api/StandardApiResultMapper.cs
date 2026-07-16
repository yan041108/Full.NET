using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Full.NET.Hosting.Api;

public sealed class StandardApiResultMapper : IApiResultMapper
{
    public IResult Map<T>(Result<T> result, HttpContext httpContext)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var error = result.Error ?? new Error(
            "common.unexpected",
            "An unexpected error occurred.",
            ErrorType.Unexpected);
        var problem = new ProblemDetails
        {
            Status = ToStatusCode(error.Type),
            Title = error.Message,
            Type = $"https://full.net/errors/{error.Code}"
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        if (error.ValidationErrors is not null)
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        return Results.Problem(problem);
    }

    public IResult MapException(Exception exception, HttpContext httpContext) =>
        Map(
            Result<object?>.Failure(new Error(
                "common.unexpected",
                "An unexpected error occurred.",
                ErrorType.Unexpected)),
            httpContext);

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
