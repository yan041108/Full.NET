using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Full.NET.Modules.Printing.Features.PublishedTemplates;
using Full.NET.Modules.Printing.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Printing;

/// <summary>控制跨模块绑定的挂起点，验证绑定期间撤权或停用必须在交付前拒绝。</summary>
[TestClass]
public sealed class PrintingPublishedTemplateServiceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    public async Task Rendering_rechecks_exact_grant_after_binding(DatabaseProvider provider, bool remainsGranted)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.CreateVersion7();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var row = new PrintingGrantedVersionRecord { TemplateId = Guid.CreateVersion7(), TemplateKey = "profile",
            TemplateName = "档案", FormSchemaKey = PrintingFormSchemaKeys.TenantProfileCard,
            VersionNumber = 1, LayoutHtml = "<section>{{tenantName}}</section>" };
        var query = Substitute.For<IQueryExecutor>();
        var statement = PrintingTenantGrantSql.ResolveVersion(provider);
        var parameters = new List<int?>();
        query.QuerySingleOrDefaultAsync<PrintingGrantedVersionRecord>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var values = (Dictionary<string, object?>)call[1]!;
                Assert.AreEqual(row.TemplateId, values["TemplateId"]);
                parameters.Add((int?)values["VersionNumber"]);
                return parameters.Count == 1 || remainsGranted ? row : null;
            });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PrintingTenantProfileBinding?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = Substitute.For<IPrintingTenantProfileBindingSource>();
        source.ResolveAsync(tenantId, Arg.Any<CancellationToken>()).Returns(_ => { entered.SetResult(); return release.Task; });
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var bindings = new PrintingFormBindingService(tenant, clock, source);
        var service = new PrintingPublishedTemplateService(tenant, query, Options.Create(new DatabaseOptions { Provider = provider }), bindings, clock);
        var preview = service.PreviewAsync(row.TemplateId, new PreviewPrintingTemplateRequest(null), new ClaimsPrincipal(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, parameters.Count); Assert.IsFalse(preview.IsCompleted);
        release.SetResult(new("A<&>", "acme", "acme.example"));
        var result = await preview.WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new int?[] { null, 1 }, parameters);
        if (remainsGranted)
        {
            Assert.IsTrue(result.IsSuccess); Assert.AreEqual("<section>A&lt;&amp;&gt;</section>", result.Value!.Html);
        }
        else
        {
            Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type); Assert.IsNull(result.Value);
        }
    }
}
