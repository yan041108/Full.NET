using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Features.Printing;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Printing.Domain;
using Full.NET.Modules.Printing.Features.PreviewTemplates;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Modules.EnterpriseRequest;
using Full.NET.Modules.Printing;
using Full.NET.Modules.Printing.Features.BrowseFormSchemas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingBusinessFormTests
{
    [TestMethod]
    public void Enterprise_module_exposes_owned_request_summary_schema()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new PrintingModule().AddServices(services, configuration);
        new EnterpriseRequestModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var schema = scope.ServiceProvider.GetRequiredService<PrintingFormSchemaQueryService>()
            .TryGet("enterprise_request.request_summary");
        Assert.IsNotNull(schema, "业务模块必须通过 Printing 契约注册固定表单。");
        CollectionAssert.AreEquivalent(new[] { "requestNumber", "title", "status", "totalAmount", "printedByDisplayName", "printedAtUtc" },
            schema.Fields.Select(field => field.FieldKey).ToArray());
    }
    [TestMethod]
    [DataRow("denied")]
    [DataRow("foreign-actor")]
    [DataRow("changed-session")]
    [DataRow("missing")]
    [DataRow("foreign-record")]
    [DataRow("success")]
    public async Task Business_binding_requires_current_session_and_owned_data_scope(string scenario)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.CreateVersion7(); var userId = Guid.CreateVersion7();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(FullNetIdentityClaimTypes.Subject, userId.ToString())], "test"));
        var actor = new AuthorizedSessionActor(userId, tenantId, Guid.CreateVersion7());
        var authorization = Substitute.For<ICurrentSessionAuthorization>();
        authorization.AuthorizeAsync(EnterpriseRequestPermissions.Read, Arg.Any<CancellationToken>()).Returns(
            scenario == "denied" ? null : scenario == "foreign-actor" ? actor with { TenantId = Guid.CreateVersion7() } : actor,
            scenario == "changed-session" ? actor with { SessionId = Guid.CreateVersion7() } : actor);
        var executor = Substitute.For<IQueryExecutor>(); var recordId = Guid.CreateVersion7();
        SqlStatement? executed = null;
        executor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executed = (SqlStatement)call[0]!;
                Assert.AreEqual(recordId, ((Dictionary<string, object?>)call[1]!)["Id"]);
                return scenario == "missing" ? null : new EnterpriseRequestRecord(recordId,
                    scenario == "foreign-record" ? Guid.CreateVersion7() : tenantId, Guid.CreateVersion7(), "R-123", "标题<&>", "Draft", 123.45m,
                    userId, 1, DateTimeOffset.UtcNow, userId, null, null, false, null, null);
            });
        var resolver = Substitute.For<IUserDataScopeResolver>(); var filters = Substitute.For<IDataScopeSqlFilterBuilder>();
        var dataScope = new EffectiveUserDataScope(false, []);
        resolver.ResolveAsync(userId, false, Arg.Any<CancellationToken>()).Returns(dataScope);
        filters.BuildOrganizationUnitFilter(dataScope, "OrganizationUnitId", userId)
            .Returns(new DataScopeSqlFilter("OrganizationUnitId = @AllowedUnit", new Dictionary<string, object?> { ["AllowedUnit"] = Guid.CreateVersion7() }));
        var queries = new EnterpriseRequestQueryService(executor, Options.Create(new DatabaseOptions()), resolver, filters);
        var source = new EnterpriseRequestPrintingBindingSource(tenant, authorization, queries);
        var result = await source.ResolveAsync(recordId, principal);
        if (scenario == "success")
        {
            Assert.IsTrue(result.IsSuccess); Assert.AreEqual("123.45", result.Value!["totalAmount"]); Assert.AreEqual("标题<&>", result.Value["title"]);
        }
        else { Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); }
        if (scenario is "denied" or "foreign-actor") Assert.IsNull(executed);
        else
        {
            Assert.IsNotNull(executed); Assert.AreEqual(SqlDataScope.TenantRequired, executed.Scope);
            Assert.AreEqual(SqlTenantBinding.CurrentTenantId, executed.TenantBinding);
            StringAssert.Contains(executed.Text, "AND TenantId = @TenantId AND (OrganizationUnitId = @AllowedUnit)");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Business_binding_rejects_missing_record_before_calling_owner(bool empty)
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme"));
        var source = Substitute.For<IPrintingRecordBindingSource>(); source.FormSchemaKey.Returns(EnterpriseRequestPrintingSchemaContributor.SchemaKey);
        var catalog = new PrintingFormSchemaCatalog([new EnterpriseRequestPrintingSchemaContributor()]);
        var bindings = new PrintingFormBindingService(tenant, Substitute.For<IClock>(), Substitute.For<IPrintingTenantProfileBindingSource>(), catalog, [source]);
        var result = await bindings.ResolveAsync(source.FormSchemaKey, new ClaimsPrincipal(), recordId: empty ? Guid.Empty : null);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Validation, result.Error!.Type);
        await source.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default);
    }

    [TestMethod]
    public void Duplicate_business_schema_key_cannot_override_binding_owner()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new PrintingFormSchemaCatalog(
            [new EnterpriseRequestPrintingSchemaContributor(), new EnterpriseRequestPrintingSchemaContributor()]));
    }

}
