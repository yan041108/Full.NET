using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Features.Printing;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Printing;

/// <summary>业务打印入口绑定原租户与会话，取消不能被空权限或空业务行覆盖。</summary>
[TestClass]
public sealed class EnterpriseRequestPrintingBoundaryTests
{
    [TestMethod]
    [DataRow("entry")]
    [DataRow("entry-invalid")]
    [DataRow("initial")]
    [DataRow("initial-null")]
    [DataRow("record")]
    [DataRow("record-null")]
    [DataRow("final")]
    [DataRow("final-null")]
    public async Task Ignored_cancellation_does_not_release_business_fields(string stage)
    {
        using var fixture = new Fixture(stage);
        if (stage.StartsWith("entry", StringComparison.Ordinal)) fixture.Cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Resolve(stage == "entry-invalid"));
        Assert.AreEqual(stage.StartsWith("entry", StringComparison.Ordinal) || stage.StartsWith("initial", StringComparison.Ordinal) ? 0 : 1,
            fixture.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("scope-initial")]
    [DataRow("scope-record")]
    [DataRow("scope-final")]
    public async Task Scope_changed_by_dependency_cannot_become_a_new_record_binding(string stage)
    {
        using var fixture = new Fixture(stage);
        var result = await fixture.Resolve();
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.IsNull(result.Value);
        Assert.AreEqual(stage == "scope-initial" ? 0 : 1, fixture.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Stable_session_retains_owned_scope_and_invariant_business_values()
    {
        using var fixture = new Fixture("allowed");
        var result = await fixture.Resolve();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("REQ-1", result.Value!["requestNumber"]);
        Assert.AreEqual("12.5", result.Value["totalAmount"]);
        Assert.AreEqual(2, fixture.Authorizations);
    }

    private sealed class Fixture : IDisposable
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public CancellationTokenSource Cancellation { get; } = new();
        public int Authorizations { get; private set; }
        private readonly Guid id = Guid.CreateVersion7();
        private readonly ClaimsPrincipal principal;
        private readonly EnterpriseRequestPrintingBindingSource source;

        public Fixture(string stage)
        {
            var tenant = new CurrentTenantAccessor(); var tenantId = Guid.CreateVersion7(); var user = Guid.CreateVersion7();
            tenant.SetTenant(new(tenantId, "acme", "Acme"));
            principal = new(new ClaimsIdentity([new Claim(FullNetIdentityClaimTypes.Subject, user.ToString("D"))], "test"));
            var actor = new AuthorizedSessionActor(user, tenantId, Guid.CreateVersion7());
            var authorization = Substitute.For<ICurrentSessionAuthorization>();
            authorization.AuthorizeAsync(EnterpriseRequestPermissions.Read, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Authorizations++;
                if (Authorizations == 1 && (stage is "initial" or "initial-null") || Authorizations == 2 && (stage is "final" or "final-null")) Cancellation.Cancel();
                if (stage == "scope-initial" && Authorizations == 1)
                { tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other")); actor = actor with { TenantId = tenant.Id }; }
                if (stage == "scope-final" && Authorizations == 2) tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                return stage == "initial-null" && Authorizations == 1 || stage == "final-null" && Authorizations == 2 ? null : actor;
            });
            Query.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "record" or "record-null") Cancellation.Cancel();
                if (stage == "scope-record") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                return stage == "record-null" ? null : new EnterpriseRequestRecord(id, tenant.Id!.Value, Guid.CreateVersion7(), "REQ-1", "Request", "Draft", 12.5m,
                    user, 1, DateTimeOffset.UtcNow, user, null, null, false, null, null);
            });
            var scopes = Substitute.For<IUserDataScopeResolver>(); var filters = Substitute.For<IDataScopeSqlFilterBuilder>();
            var scope = new EffectiveUserDataScope(false, []);
            scopes.ResolveAsync(user, false, Arg.Any<CancellationToken>()).Returns(scope);
            filters.BuildOrganizationUnitFilter(scope, "OrganizationUnitId", user).Returns(new DataScopeSqlFilter("OrganizationUnitId = @AllowedUnit",
                new Dictionary<string, object?> { ["AllowedUnit"] = Guid.CreateVersion7() }));
            source = new(tenant, authorization, new(Query, Options.Create(new DatabaseOptions()), scopes, filters));
        }
        public Task<Result<IReadOnlyDictionary<string, string?>>> Resolve(bool invalid = false) =>
            source.ResolveAsync(invalid ? Guid.Empty : id, principal, Cancellation.Token);
        public void Dispose() => Cancellation.Dispose();
    }
}
