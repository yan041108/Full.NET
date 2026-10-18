using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Document;

/// <summary>Host 取消只接受真实 Host 交互会话；请求中的机器或租户声明不能绕过上下文。</summary>
[TestClass]
public sealed class DocumentPreviewHttpSessionBindingTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("oidc")]
    [DataRow("api-key")]
    [DataRow("anonymous")]
    [DataRow("tenant-context")]
    [DataRow("tenant-scope")]
    [DataRow("tenant-actor")]
    [DataRow("missing-stamp")]
    [DataRow("missing-session")]
    [DataRow("empty-subject")]
    [DataRow("empty-session")]
    public void Only_host_interactive_sessions_form_binding(string scenario)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        if (scenario == "tenant-context") tenant.SetTenant(new TenantContext(Guid.NewGuid(), "acme", "Acme"));
        var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new("sub", (scenario == "empty-subject" ? Guid.Empty : userId).ToString("D")),
            new(FullNetIdentityClaimTypes.SessionId, (scenario == "empty-session" ? Guid.Empty : sessionId).ToString("D")),
            new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"),
            new(FullNetIdentityClaimTypes.ActorScope, scenario == "tenant-actor" ? "tenant:other" : "host"),
            new(FullNetIdentityClaimTypes.Scope, scenario == "tenant-scope" ? "tenant:other" : "host"),
        };
        if (scenario == "oidc")
        {
            claims.Add(new(FullNetIdentityClaimTypes.TokenUse, "access"));
            claims.Add(new(FullNetIdentityClaimTypes.ApplicationSessionId, sessionId.ToString("D")));
        }
        if (scenario == "api-key") claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.NewGuid().ToString("D")));
        if (scenario == "missing-stamp") claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.SecurityStamp);
        if (scenario == "missing-session") claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.SessionId);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous" ? null : "test")) };
        var accepted = DocumentPreviewHttpSessionBinding.TryCreate(context, tenant, out var binding);
        Assert.AreEqual(scenario is "valid" or "oidc", accepted);
        if (accepted)
        {
            Assert.IsNull(binding.TenantId); Assert.AreEqual(userId, binding.UserId); Assert.AreEqual(sessionId, binding.SessionId);
            Assert.AreEqual(scenario == "oidc" ? SessionBindingKinds.OidcApplication : SessionBindingKinds.Refresh, binding.SessionKind);
        }
    }
}
