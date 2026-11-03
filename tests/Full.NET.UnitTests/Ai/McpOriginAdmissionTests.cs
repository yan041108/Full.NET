using Full.NET.AgenticWeb.Mcp;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Full.NET.UnitTests.Ai;

/// <summary>MCP 的 Origin 拒绝必须复用既有精确白名单，并覆盖所有传输方法。</summary>
[TestClass]
public sealed class McpOriginAdmissionTests
{
    [TestMethod]
    [DataRow("POST", "", true)]
    [DataRow("POST", "http://approved.test", true)]
    [DataRow("GET", "http://attacker.test", false)]
    [DataRow("OPTIONS", "http://attacker.test", false)]
    [DataRow("POST", "http://attacker.test", false)]
    [DataRow("POST", "null", false)]
    [DataRow("POST", "http://approved.test/path", false)]
    [DataRow("POST", "http://approved.test,http://attacker.test", false)]
    public async Task Existing_origin_policy_is_enforced(string method, string origin, bool allowed)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCors(options => options.AddPolicy(IdentityBrowserCorsPolicy.Name,
            policy => policy.WithOrigins("http://approved.test")));
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = "/api/v1/ai/mcp"; context.Request.Method = method;
        // 即便 Origin 与 Host 相同，未获批准也不能绕过防 DNS 重绑定校验。
        context.Request.Host = new HostString("attacker.test"); context.Request.Scheme = "http";
        if (origin.Length > 0) context.Request.Headers.Origin = origin;
        var reached = false;
        await McpOriginAdmission.InvokeAsync(context, _ => { reached = true; return Task.CompletedTask; });
        Assert.AreEqual(allowed, reached);
        if (!allowed) Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task Duplicate_origin_and_missing_policy_fail_closed()
    {
        var services = new ServiceCollection().AddLogging().AddCors();
        await using var provider = services.BuildServiceProvider();
        foreach (var header in new[] { new StringValues(["http://a.test", "http://b.test"]), new StringValues("http://a.test") })
        {
            var context = new DefaultHttpContext { RequestServices = provider };
            context.Request.Path = "/api/v1/ai/mcp/"; context.Request.Headers.Origin = header;
            var reached = false;
            await McpOriginAdmission.InvokeAsync(context, _ => { reached = true; return Task.CompletedTask; });
            Assert.IsFalse(reached);
            Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }
    }
}
