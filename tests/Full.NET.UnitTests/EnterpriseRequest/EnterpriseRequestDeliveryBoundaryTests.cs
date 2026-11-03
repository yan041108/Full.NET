using System.IO.Compression;
using System.Security.Claims;
using Full.NET.Modules.EnterpriseRequest.Features.Delivery;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Features.RenderForms;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.RenderWorkbooks;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>新交付边界只验证真实安全约束，不复制服务内部状态机。</summary>
[TestClass]
public sealed class EnterpriseRequestDeliveryBoundaryTests
{
    [TestMethod]
    public void Workbook_uses_text_cells_and_escapes_formulas()
    {
        var result = new ReportingWorkbookRenderer().Render([new("title", "Title")],
            [new(new Dictionary<string, string?> { ["title"] = "=HYPERLINK(\"evil\")" })]);
        Assert.IsTrue(result.IsSuccess);
        using var archive = new ZipArchive(new MemoryStream(result.Value!));
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = reader.ReadToEnd();
        StringAssert.Contains(xml, "'=HYPERLINK");
        Assert.IsFalse(xml.Contains("<f>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workbook_rejects_unknown_columns_and_excess_rows()
    {
        var renderer = new ReportingWorkbookRenderer();
        ReportingExecutionRow row = new(new Dictionary<string, string?> { ["other"] = "secret" });
        Assert.IsFalse(renderer.Render([new("title", "Title")], [row]).IsSuccess);
        Assert.IsFalse(renderer.Render([new("title", "Title")], Enumerable.Repeat(row, 5001).ToArray()).IsSuccess);
        Assert.IsFalse(renderer.Render([new("title", "A"), new("title", "B")], []).IsSuccess);
    }

    [TestMethod]
    public void Printing_encodes_values_and_rejects_missing_bindings_and_budgets()
    {
        var renderer = new PrintingFormRenderer();
        var result = renderer.Render("<p>{{title}}</p>", new Dictionary<string, string?> { ["title"] = "<img src=x onerror=alert(1)>" });
        Assert.IsTrue(result.IsSuccess);
        StringAssert.Contains(result.Value!, "&lt;img");
        Assert.IsFalse(result.Value!.Contains("<img", StringComparison.Ordinal));
        Assert.IsFalse(renderer.Render("{{missing}}", new Dictionary<string, string?>()).IsSuccess);
        Assert.IsFalse(renderer.Render(new string('x', 65537), new Dictionary<string, string?>()).IsSuccess);
    }

    [TestMethod]
    public void Renderers_observe_cancellation_before_work()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => new ReportingWorkbookRenderer().Render([], [], cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => new PrintingFormRenderer().Render("", new Dictionary<string, string?>(), cancellation.Token));
    }

    [TestMethod]
    public void Session_binding_rejects_machine_identity_wrong_tenant_and_malformed_oidc()
    {
        var tenant = Guid.CreateVersion7();
        var claims = new[] { new Claim(FullNetIdentityClaimTypes.Subject, Guid.CreateVersion7().ToString()),
            new Claim(FullNetIdentityClaimTypes.TenantId, tenant.ToString()),
            new Claim(FullNetIdentityClaimTypes.SessionId, Guid.CreateVersion7().ToString()),
            new Claim(FullNetIdentityClaimTypes.SecurityStamp, "stamp"), new Claim(FullNetIdentityClaimTypes.ActorScope, "host"),
            new Claim(FullNetIdentityClaimTypes.Scope, $"tenant:{tenant:N}") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        Assert.IsTrue(EnterpriseRequestSessionBinding.TryRead(principal, tenant, out _));
        Assert.IsFalse(EnterpriseRequestSessionBinding.TryRead(principal, Guid.CreateVersion7(), out _));
        Assert.IsFalse(EnterpriseRequestSessionBinding.TryRead(new(new ClaimsIdentity(claims, "FullNET.Signature")), tenant, out _));
        principal.AddIdentity(new ClaimsIdentity([new Claim(FullNetIdentityClaimTypes.ApplicationSessionId, "invalid")]));
        Assert.IsFalse(EnterpriseRequestSessionBinding.TryRead(principal, tenant, out _));
    }
}
