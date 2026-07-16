using Full.NET.Abstractions.Results;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Hosting.Api;

public interface IApiResultMapper
{
    IResult Map<T>(Result<T> result, HttpContext httpContext);

    IResult MapException(Exception exception, HttpContext httpContext);
}
