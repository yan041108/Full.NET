using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Reporting;

/// <summary>创建、下载与取消复用同一受信 HTTP 会话边界，机器凭据不得伪装交互会话。</summary>
[TestClass]
public sealed class ReportingHttpSessionBindingTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("oidc")]
    [DataRow("api-key")]
    [DataRow("missing-stamp")]
    [DataRow("empty-session")]
    [DataRow("anonymous")]
    [DataRow("host")]
    [DataRow("missing-scope")]
    public void Only_authenticated_tenant_interactive_sessions_can_form_binding(string scenario)
    {
        var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new("sub", userId.ToString("D")),
            new(FullNetIdentityClaimTypes.SessionId, (scenario == "empty-session" ? Guid.Empty : sessionId).ToString("D")),
            new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"),
            new(FullNetIdentityClaimTypes.ActorScope, "host"),
            new(FullNetIdentityClaimTypes.Scope, $"tenant:{tenantId:N}"),
        };
        if (scenario == "oidc")
        {
            claims.Add(new(FullNetIdentityClaimTypes.ApplicationSessionId, sessionId.ToString("D")));
            claims.Add(new(FullNetIdentityClaimTypes.TokenUse, "access"));
        }
        if (scenario == "api-key") claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.NewGuid().ToString("D")));
        if (scenario == "missing-stamp") claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.SecurityStamp);
        if (scenario == "missing-scope") claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.Scope);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous" ? null : "test")) };
        var accepted = ReportingHttpSessionBinding.TryCreate(context, scenario == "host" ? null : tenantId, out var binding);
        Assert.AreEqual(scenario is "valid" or "oidc", accepted);
        if (accepted)
        {
            Assert.AreEqual(userId, binding.UserId); Assert.AreEqual(tenantId, binding.TenantId); Assert.AreEqual(sessionId, binding.SessionId);
            Assert.AreEqual(scenario == "oidc" ? SessionBindingKinds.OidcApplication : SessionBindingKinds.Refresh, binding.SessionKind);
        }
    }
}
