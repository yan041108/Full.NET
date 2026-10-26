using System.Resources;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Serialization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Hosting;

/// <summary>单元测试（Hosting）：Standard Api Result Mapper。</summary>
[TestClass]
public sealed class StandardApiResultMapperTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Validation_error_extensions_serialize_with_generated_metadata_only(bool structured)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().AddFullNetJson();
        services.ConfigureHttpJsonOptions(options =>
        {
            foreach (var resolver in options.SerializerOptions.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().ToArray())
                options.SerializerOptions.TypeInfoResolverChain.Remove(resolver);
        });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        using var body = new MemoryStream(); context.Response.Body = body;
        var mapped = CreateMapper().Map(Result<string>.Failure(new Error(
            Code: ValidationErrorCodes.Failed,
            Message: "Validation failed.",
            Type: ErrorType.Validation,
            ValidationErrors: new Dictionary<string, string[]> { ["publication"] = ["workflow.form_runtime_mismatch"] },
            Arguments: null,
            ValidationViolations: structured ? [new ValidationViolation("publication", ValidationErrorCodes.Required, new Dictionary<string, object?>())] : null)), context);
        // 执行 ASP.NET Core 自带 ProblemDetails writer；只序列化外层字典不能复现其元数据闭包。
        await mapped.ExecuteAsync(context);
        using var json = JsonDocument.Parse(body.ToArray());
        Assert.AreEqual(400, context.Response.StatusCode);
        Assert.AreEqual(1, json.RootElement.GetProperty("errors").GetProperty("publication").GetArrayLength());
        Assert.AreEqual(ValidationErrorCodes.Failed, json.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(structured, json.RootElement.TryGetProperty("violations", out _));
    }

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
        var mapper = CreateMapper();

        var mapped = mapper.Map(
            Result<string>.Failure(new Error(
                Code: "test.error",
                Message: "Test error.",
                Type: errorType)),
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
        var context = new DefaultHttpContext();
        var mapped = CreateMapper().Map(Result<string>.Success("ok"), context);

        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)mapped).StatusCode);
        Assert.AreEqual("ok", ((IValueHttpResult)mapped).Value);
        Assert.IsFalse(context.Response.Headers.ContainsKey("Content-Language"));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Vary"));
    }

    [TestMethod]
    public void Exception_maps_to_sanitized_500()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper().MapException(
            new InvalidOperationException("sensitive"),
            context);

        Assert.AreEqual(
            StatusCodes.Status500InternalServerError,
            ((IStatusCodeHttpResult)mapped).StatusCode);
        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        Assert.AreEqual(CommonErrorCodes.Unexpected, problem.Extensions["code"]);
        Assert.DoesNotContain(
            "sensitive",
            problem.Title ?? string.Empty,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Database_capacity_exception_maps_to_sanitized_503_with_retry_after()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper().MapException(
            new ServiceCapacityExceededException(
                ServiceCapacityFailureKind.Rejected,
                TimeSpan.FromMilliseconds(250)),
            context);

        Assert.AreEqual(
            StatusCodes.Status503ServiceUnavailable,
            ((IStatusCodeHttpResult)mapped).StatusCode);
        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        Assert.AreEqual(
            CommonErrorCodes.DatabaseCapacityExhausted,
            problem.Extensions["code"]);
        Assert.AreEqual(context.TraceIdentifier, problem.Extensions["traceId"]);
        Assert.AreEqual("1", context.Response.Headers.RetryAfter.ToString());
        Assert.DoesNotContain(
            "250",
            problem.Title ?? string.Empty,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validation_failure_exposes_structured_violations_and_language_headers()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper().Map(
            Result<string>.Failure(new Error(
                Code: ValidationErrorCodes.Failed,
                Message: "One or more validation errors occurred.",
                Type: ErrorType.Validation,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["Username"] = ["Username is required."],
                },
                Arguments: null,
                ValidationViolations:
                [
                    new ValidationViolation(
                        "Username",
                        ValidationErrorCodes.Required,
                        new Dictionary<string, object?>()),
                ])),
            context);

        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        Assert.IsTrue(problem.Extensions.ContainsKey("violations"));
        var errors = (IReadOnlyDictionary<string, string[]>)problem.Extensions["errors"]!;
        CollectionAssert.AreEqual(new[] { "该字段为必填项。" }, errors["Username"]);
        Assert.AreEqual("zh-CN", context.Response.Headers.ContentLanguage.ToString());
        StringAssert.Contains(
            context.Response.Headers.Vary.ToString(),
            "Accept-Language",
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Validation_error_messages_are_not_dropped_when_producer_counts_mismatch()
    {
        var context = new DefaultHttpContext();
        var mapped = CreateMapper().Map(
            Result<string>.Failure(new Error(
                Code: ValidationErrorCodes.Failed,
                Message: "One or more validation errors occurred.",
                Type: ErrorType.Validation,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["Password"] =
                    [
                        "Password is required.",
                        "Unpaired legacy policy message.",
                    ],
                },
                Arguments: null,
                ValidationViolations:
                [
                    new ValidationViolation(
                        "Password",
                        ValidationErrorCodes.Required,
                        new Dictionary<string, object?>()),
                ])),
            context);

        var problem = (ProblemDetails?)((IValueHttpResult)mapped).Value;
        Assert.IsNotNull(problem);
        var errors = (IReadOnlyDictionary<string, string[]>)problem.Extensions["errors"]!;
        CollectionAssert.AreEqual(
            new[] { "该字段为必填项。", "Unpaired legacy policy message." },
            errors["Password"]);
    }

    private static StandardApiResultMapper CreateMapper(string locale = "zh-CN")
    {
        var resources = new ResourceManager(
            "Full.NET.Hosting.Resources.CommonErrors",
            typeof(StandardApiResultMapper).Assembly);
        IErrorResourceSource[] sources =
        [
            new ResourceManagerErrorResourceSource(CommonErrorCodes.Prefix, resources),
            new ResourceManagerErrorResourceSource(
                CommonErrorCodes.AuthorizationPrefix,
                resources),
            new ResourceManagerErrorResourceSource(ValidationErrorCodes.Prefix, resources),
        ];
        return new StandardApiResultMapper(
            new ResourceErrorMessageLocalizer(sources, new NamedMessageFormatter()),
            new StubLocaleContext(locale));
    }

    private sealed class StubLocaleContext(string locale) : ILocaleContext
    {
        public string CurrentLocale => locale;
    }
}
