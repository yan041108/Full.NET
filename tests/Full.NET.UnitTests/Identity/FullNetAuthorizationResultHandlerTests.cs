using System.Text.Json;
using System.Globalization;
using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class FullNetAuthorizationResultHandlerTests
{
    [TestMethod]
    public async Task Challenged_result_preserves_authentication_header_and_writes_problem_details()
    {
        var context = new DefaultHttpContext();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddProblemDetails();
        serviceCollection.AddSingleton<IAuthenticationService>(
            new RecordingAuthenticationService());
        using var services = serviceCollection.BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        var handler = new FullNetAuthorizationResultHandler(
            new StandardApiResultMapper(
                new StubErrorMessageLocalizer(),
                new StubLocaleContext()));

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy,
            PolicyAuthorizationResult.Challenge());

        Assert.AreEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.AreEqual("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.AreEqual(
            IdentityErrorCodes.SessionNotActive,
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
        Assert.AreEqual("当前会话已失效。", document.RootElement
            .GetProperty("title").GetString());
        Assert.AreEqual("zh-CN", context.Response.Headers.ContentLanguage.ToString());
    }

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
        var handler = new FullNetAuthorizationResultHandler(
            new StandardApiResultMapper(
                new StubErrorMessageLocalizer(),
                new StubLocaleContext()));

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
        Assert.AreEqual("当前身份没有所需权限。", document.RootElement
            .GetProperty("title").GetString());
        Assert.AreEqual("zh-CN", context.Response.Headers.ContentLanguage.ToString());
    }

    private sealed class StubErrorMessageLocalizer : IErrorMessageLocalizer
    {
        public string Localize(Error error, CultureInfo culture) =>
            error.Code == CommonErrorCodes.PermissionDenied
                ? "当前身份没有所需权限。"
                : error.Code == IdentityErrorCodes.SessionNotActive
                    ? "当前会话已失效。"
                : error.DefaultMessage;
    }

    private sealed class StubLocaleContext : ILocaleContext
    {
        public string CurrentLocale => "zh-CN";
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
