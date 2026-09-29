using Full.NET.Modules.Auditing.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AccessLogRouteTests
{
    [TestMethod]
    public async Task Exception_handler_keeps_original_route_available_to_outer_access_log()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(new System.Diagnostics.DiagnosticListener("test"));
        services.AddMetrics();
        services.AddProblemDetails();
        using var provider = services.BuildServiceProvider();

        string? capturedRoute = null;
        var app = new ApplicationBuilder(provider);
        app.Use(async (context, next) =>
        {
            await next(context);
            capturedRoute = AccessLogMiddleware.ResolveRouteTemplate(context);
        });
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        }));
        app.Run(context =>
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/v1/orders/{id}"),
                order: 0,
                EndpointMetadataCollection.Empty,
                "orders"));
            throw new InvalidOperationException("mapped error");
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/orders/123";
        await app.Build()(httpContext);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.AreEqual("/api/v1/orders/{id}", capturedRoute);
    }
}
