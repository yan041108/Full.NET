using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class FullNetExceptionHandlerLogTests
{
    [TestMethod]
    public async Task Exception_log_uses_route_template_without_secret_path_segment()
    {
        var logger = new CaptureLogger();
        var handler = new FullNetExceptionHandler(new StatusCodeMapper(), logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders/token-secret";
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/orders/{id}"),
            0,
            EndpointMetadataCollection.Empty,
            "order"));

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("token-secret"),
            CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.AreEqual("/api/v1/orders/{id}", logger.RequestPath);
        Assert.IsFalse(logger.FormattedMessage.Contains("token-secret", StringComparison.Ordinal));
        Assert.IsNull(logger.CapturedException);
    }

    [TestMethod]
    public async Task Unmatched_exception_log_uses_fixed_placeholder()
    {
        var logger = new CaptureLogger();
        var handler = new FullNetExceptionHandler(new StatusCodeMapper(), logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/token-secret";

        await handler.TryHandleAsync(
            context,
            new InvalidOperationException("token-secret"),
            CancellationToken.None);

        Assert.AreEqual("<unmatched>", logger.RequestPath);
        Assert.IsFalse(logger.FormattedMessage.Contains("token-secret", StringComparison.Ordinal));
        Assert.IsNull(logger.CapturedException);
    }

    private sealed class StatusCodeMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            new FixedStatusResult(StatusCodes.Status200OK);

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            new FixedStatusResult(StatusCodes.Status500InternalServerError);
    }

    private sealed class FixedStatusResult(int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILogger<FullNetExceptionHandler>
    {
        public string? RequestPath { get; private set; }

        public string FormattedMessage { get; private set; } = string.Empty;

        public Exception? CapturedException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            FormattedMessage = formatter(state, exception);
            CapturedException = exception;
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                RequestPath = properties.FirstOrDefault(
                    property => property.Key == "RequestPath").Value?.ToString();
            }
        }
    }
}
