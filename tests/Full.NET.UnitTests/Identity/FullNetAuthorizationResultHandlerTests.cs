using System.Text.Json;
using Full.NET.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class FullNetAuthorizationResultHandlerTests
{
    [TestMethod]
    public async Task Forbidden_result_writes_standard_problem_details()
    {
        var context = new DefaultHttpContext();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddProblemDetails();
        using var services = serviceCollection.BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        var handler = new FullNetAuthorizationResultHandler();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy,
            PolicyAuthorizationResult.Forbid());

        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.AreEqual(
            "authorization.permission_denied",
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
    }
}
