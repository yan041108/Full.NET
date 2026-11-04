using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.ManageTemplates;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Full.NET.Modules.Printing.Features.PublishedTemplates;
using Full.NET.Modules.Printing.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Printing;

/// <summary>Host 和租户预览都拒绝不完整或超预算响应，取消不变成普通布局失败。</summary>
[TestClass]
public sealed class PrintingPreviewBudgetTests
{
    [TestMethod]
    [DataRow(false, "binding")]
    [DataRow(true, "binding")]
    [DataRow(false, "layout")]
    [DataRow(true, "layout")]
    [DataRow(false, "output")]
    [DataRow(true, "output")]
    public async Task Preview_returns_stable_validation_failure_without_partial_html(bool host, string kind)
    {
        var fixture = new Fixture(host, kind);
        var result = await fixture.Preview(default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value);
        Assert.AreEqual(kind == "binding" ? PrintingErrorCodes.BindingFailed : PrintingErrorCodes.LayoutHtmlInvalid, result.Error!.Code);
    }

    [TestMethod]
    [DataRow(false, "binding")]
    [DataRow(true, "binding")]
    [DataRow(false, "query")]
    [DataRow(true, "query")]
    public async Task Preview_propagates_ignored_cancellation_after_async_boundaries(bool host, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(host, "normal", stage == "binding" ? cancellation.Cancel : null, stage == "query" ? cancellation.Cancel : null);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Preview(cancellation.Token));
    }

    [TestMethod]
    public async Task Null_final_grant_does_not_turn_cancellation_into_permission_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(false, "normal", afterQuery: cancellation.Cancel, nullFinalGrant: true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Preview(cancellation.Token));
    }

    private sealed class Fixture
    {
        private readonly PrintingTemplatePreviewService host;
        private readonly PrintingPublishedTemplateService tenant;
        private readonly bool useHost;
        private readonly Guid id = Guid.CreateVersion7();
        public Fixture(bool useHost, string kind, Action? afterBinding = null, Action? afterQuery = null, bool nullFinalGrant = false)
        {
            this.useHost = useHost;
            var layout = kind == "layout" ? new string('x', PrintingLayoutPolicy.MaxLayoutHtmlLength + 1)
                : kind == "output" ? string.Concat(Enumerable.Repeat("{{tenantName}}", 1500)) : "<p>{{tenantName}}</p>";
            var name = kind == "binding" ? new string('x', (int)(PrintingLayoutPolicy.MaxBindingTextBytes / 2) + 1)
                : kind == "output" ? new string('&', 200) : "Acme";
            var context = new CurrentTenantAccessor(); context.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme"));
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            var source = Substitute.For<IPrintingTenantProfileBindingSource>();
            source.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { afterBinding?.Invoke(); return new PrintingTenantProfileBinding(name, "acme", "acme.example"); });
            var bindings = new PrintingFormBindingService(context, clock, source, new([]), []);
            var query = Substitute.For<IQueryExecutor>();
            query.QuerySingleOrDefaultAsync<PrintingTemplateRecord>(PrintingTemplateSql.FindTemplateById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new PrintingTemplateRecord { Id = id, TemplateKey = "profile", Name = "Profile", FormSchemaKey = PrintingFormSchemaKeys.TenantProfileCard, IsEnabled = true, LatestPublishedVersionNumber = 1 });
            query.QuerySingleOrDefaultAsync<PrintingTemplateVersionRecord>(PrintingTemplateSql.FindVersionByNumber, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { afterQuery?.Invoke(); return new PrintingTemplateVersionRecord { TemplateId = id, VersionNumber = 1, LayoutHtml = layout }; });
            var grants = 0;
            query.QuerySingleOrDefaultAsync<PrintingGrantedVersionRecord>(PrintingTenantGrantSql.ResolveVersion(DatabaseProvider.SqlServer), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { if (++grants == 2) { afterQuery?.Invoke(); if (nullFinalGrant) return null; } return new PrintingGrantedVersionRecord { TemplateId = id, TemplateKey = "profile", TemplateName = "Profile", FormSchemaKey = PrintingFormSchemaKeys.TenantProfileCard, VersionNumber = 1, LayoutHtml = layout }; });
            host = new(new(query), bindings, clock);
            tenant = new(context, query, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), bindings, clock, new([]));
        }
        public Task<Full.NET.Abstractions.Results.Result<PrintingTemplatePreviewResponse>> Preview(CancellationToken token) => useHost
            ? host.PreviewAsync(id, new(null), new ClaimsPrincipal(), token) : tenant.PreviewAsync(id, new(null), new ClaimsPrincipal(), token);
    }
}
