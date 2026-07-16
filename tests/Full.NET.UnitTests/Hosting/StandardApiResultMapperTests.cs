using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class StandardApiResultMapperTests
{
    [TestMethod]
    [DataRow(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [DataRow(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [DataRow(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [DataRow(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [DataRow(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [DataRow(ErrorType.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    [DataRow(ErrorType.RateLimited, StatusCodes.Status429TooManyRequests)]
    [DataRow(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    public void Failure_maps_to_expected_status_and_problem_details(
        ErrorType errorType,
        int expectedStatus)
    {
        var context = new DefaultHttpContext();
        var mapper = new StandardApiResultMapper();

        var mapped = mapper.Map(
            Result<string>.Failure(new Error("test.error", "Test error.", errorType)),
            context);

        Assert.AreEqual(expectedStatus, ((IStatusCodeHttpResult)mapped).StatusCode);
        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        Assert.AreEqual("test.error", problem.Extensions["code"]);
        Assert.AreEqual(context.TraceIdentifier, problem.Extensions["traceId"]);
    }

    [TestMethod]
    public void Success_maps_to_status_200_and_raw_value()
    {
        var mapped = new StandardApiResultMapper().Map(
            Result<string>.Success("ok"),
            new DefaultHttpContext());

        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)mapped).StatusCode);
        Assert.AreEqual("ok", ((IValueHttpResult)mapped).Value);
    }

    [TestMethod]
    public void Exception_maps_to_sanitized_500()
    {
        var context = new DefaultHttpContext();
        var mapped = new StandardApiResultMapper().MapException(
            new InvalidOperationException("sensitive"),
            context);

        Assert.AreEqual(
            StatusCodes.Status500InternalServerError,
            ((IStatusCodeHttpResult)mapped).StatusCode);
        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        Assert.AreEqual("common.unexpected", problem.Extensions["code"]);
        Assert.DoesNotContain(
            "sensitive",
            problem.Title ?? string.Empty,
            StringComparison.Ordinal);
    }
}
