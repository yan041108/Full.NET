using System.Security.Claims;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>后台导入冻结最小能力，恢复时必须保留可信会话绑定。</summary>
[TestClass]
public sealed class ImportExportExecutionAuthorizationTests
{
    [TestMethod]
    public void Execution_state_round_trip_preserves_session_and_checkpoint_rows()
    {
        var binding = Binding();
        var row = new StaticImportRowExecutionResult(7, true, Guid.NewGuid(), null, null);
        var state = new ImportExportExecutionStateDocument(new Dictionary<string, bool> { ["assign"] = true }, [row], binding);
        var restored = ImportExportTaskMapper.DeserializeExecutionState(ImportExportTaskMapper.SerializeExecutionState(state));
        Assert.AreEqual(binding, restored.SessionBinding); Assert.AreEqual(row, restored.Rows.Single());
        Assert.IsTrue(restored.CapabilityFlags!["assign"]);
        Assert.IsNull(ImportExportTaskMapper.DeserializeExecutionState("{\"capabilityFlags\":{},\"rows\":[]}").SessionBinding);
    }

    [TestMethod]
    public async Task Only_declared_previously_granted_capabilities_are_reauthorized()
    {
        var binding = Binding(); var handler = Substitute.For<IStaticImportSchemaHandler>();
        handler.GetDefinition().Returns(new StaticImportSchemaDefinition("test", "test", "tenant", "test.import", []));
        handler.ExecutionCapabilityPermissions.Returns(["assign", "read"]);
        var frozen = ImportExportExecutionAuthorization.FreezeCapabilities(handler,
            new Dictionary<string, bool> { ["assign"] = true, ["unrelated"] = true });
        Assert.AreEqual(2, frozen.Count); Assert.IsTrue(frozen["assign"]); Assert.IsFalse(frozen["read"]);
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(binding.UserId, binding.TenantId, binding.SessionId));
        Assert.IsTrue(await new ImportExportExecutionAuthorization(identity).IsAllowedAsync(binding,
            binding.TenantId!.Value, handler, frozen, default));
        var permissions = identity.ReceivedCalls().Select(call => (string)call.GetArguments()[1]!).ToArray();
        CollectionAssert.AreEquivalent(new[] { ImportExportPermissions.ImportTasksExecute, "test.import", "assign" }, permissions);
    }

    /// <summary>后台验证返回其他主体时也必须拒绝，不把接口返回非空视为充分授权。</summary>
    [TestMethod]
    public async Task Identity_actor_mismatch_is_rejected()
    {
        var binding = Binding(); var handler = Substitute.For<IStaticImportSchemaHandler>();
        handler.GetDefinition().Returns(new StaticImportSchemaDefinition("test", "test", "tenant", "test.import", []));
        handler.ExecutionCapabilityPermissions.Returns([]);
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(Guid.NewGuid(), binding.TenantId, binding.SessionId));
        Assert.IsFalse(await new ImportExportExecutionAuthorization(identity).IsAllowedAsync(binding,
            binding.TenantId!.Value, handler, null, default));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("oidc")]
    [DataRow("api-key")]
    [DataRow("missing-stamp")]
    [DataRow("empty-session")]
    [DataRow("anonymous")]
    public void Http_binding_accepts_only_authenticated_session_subjects(string scenario)
    {
        var binding = Binding();
        var claims = new List<Claim>
        {
            new("sub", binding.UserId.ToString("D")),
            new(FullNetIdentityClaimTypes.SessionId, binding.SessionId.ToString("D")),
            new(FullNetIdentityClaimTypes.SecurityStamp, binding.SecurityStamp),
            new(FullNetIdentityClaimTypes.ActorScope, binding.ActorScope),
            new(FullNetIdentityClaimTypes.Scope, binding.EffectiveScope),
        };
        if (scenario == "oidc")
        {
            claims.Add(new(FullNetIdentityClaimTypes.ApplicationSessionId, binding.SessionId.ToString("D")));
            claims.Add(new(FullNetIdentityClaimTypes.TokenUse, "access"));
        }
        if (scenario == "api-key") claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.NewGuid().ToString("D")));
        if (scenario == "missing-stamp") claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.SecurityStamp);
        if (scenario == "empty-session")
        {
            claims.RemoveAll(claim => claim.Type == FullNetIdentityClaimTypes.SessionId);
            claims.Add(new(FullNetIdentityClaimTypes.SessionId, Guid.Empty.ToString("D")));
        }
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous" ? null : "test")) };
        var accepted = ImportExportHttpSessionBinding.TryCreate(context, binding.TenantId, out var actual);
        Assert.AreEqual(scenario is "valid" or "oidc", accepted);
        if (accepted)
        {
            Assert.AreEqual(binding.UserId, actual.UserId); Assert.AreEqual(binding.TenantId, actual.TenantId);
            Assert.AreEqual(scenario == "oidc" ? SessionBindingKinds.OidcApplication : SessionBindingKinds.Refresh, actual.SessionKind);
        }
    }

    private static SessionBindingSnapshot Binding()
    {
        var tenantId = Guid.NewGuid();
        return new(Guid.NewGuid(), tenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
    }
}
