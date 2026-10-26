using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageUnitLeadership;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>领导权只能由精确权限配置，身份准备不得进入本地事务，机构范围或修订变化必须拒绝。</summary>
[TestClass]
public sealed class OrganizationUnitLeadershipTests
{
    [TestMethod]
    [DataRow("GET", "", OrganizationUnitLeadershipPermissions.Read)]
    [DataRow("PUT", "", OrganizationUnitLeadershipPermissions.Update)]
    [DataRow("GET", "/candidates", OrganizationUnitLeadershipPermissions.Update)]
    public async Task Endpoints_require_separate_exact_permissions(string method, string suffix, string permission)
    {
        var builder = WebApplication.CreateBuilder(); var module = new OrganizationModule(); module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>()); await using var app = builder.Build();
        Full.NET.Modules.Organization.Features.ManageUnitLeadership.Endpoint.Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/api/v1/organization/units/{unitId:guid}/leadership" + suffix &&
                candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(); Assert.HasCount(1, policies);
        Assert.AreEqual(FullNetPermissionPolicies.For(permission), policies[0].Policy);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task First_assignment_checks_identity_outside_transaction_and_binds_the_actual_actor(DatabaseProvider provider)
    {
        var f = new Fixture(provider); var result = await f.Service.SetAsync(f.Unit, f.Actor, false, new(f.Leader, 3, 0), default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.Leader, result.Value!.LeaderUserId); Assert.AreEqual(1L, result.Value.Revision);
        Assert.AreEqual(f.Actor, result.Value.UpdatedById); Assert.AreEqual(0L, result.Value.UpdatedAtUtc!.Value.UtcTicks % 10);
        Assert.IsFalse(f.MembersQueriedInsideTransaction);
        await f.Commands.Received(1).ExecuteAsync(OrganizationUnitLeadershipSql.Insert,
            Arg.Is<object?>(value => Has(value, "ActorUserId", f.Actor) && Has(value, "LeaderUserId", f.Leader) && Has(value, "UnitVersion", 3)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Foreign_or_revoked_member_never_starts_a_write_transaction()
    {
        var f = new Fixture(); f.Members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, TenantUserDirectoryEntry>());
        var result = await f.Service.SetAsync(f.Unit, f.Actor, false, new(f.Leader, 3, 0), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(OrganizationErrorCodes.UnitLeaderInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Transaction.Calls); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Unit_scope_or_configuration_revision_change_cannot_reuse_prepared_authority(bool movedUnit)
    {
        var f = new Fixture();
        if (movedUnit) f.Query.QuerySingleOrDefaultAsync<OrganizationLeadershipUnitRecord>(OrganizationUnitLeadershipSql.LockUnitSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new OrganizationLeadershipUnitRecord(f.Unit, true, 4));
        else f.Query.QuerySingleOrDefaultAsync<OrganizationUnitLeadershipRecord>(OrganizationUnitLeadershipSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationUnitLeadershipRecord(f.Unit, f.Leader, 2, f.Actor, DateTimeOffset.UtcNow));
        var result = await f.Service.SetAsync(f.Unit, f.Actor, false, new(f.Leader, 3, 0), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(OrganizationErrorCodes.UnitLeadershipConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_data_scope_returns_not_found_before_identity_lookup()
    {
        var f = new Fixture(); f.Query.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(OrganizationSql.FindUnitById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((OrganizationUnitRecord?)null);
        var result = await f.Service.SetAsync(f.Unit, f.Actor, false, new(f.Leader, 3, 0), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(OrganizationErrorCodes.UnitNotFound, result.Error!.Code);
        Assert.AreEqual(0, f.Members.ReceivedCalls().Count()); Assert.AreEqual(0, f.Transaction.Calls);
    }

    private static bool Has(object? value, string key, object expected) => value is IReadOnlyDictionary<string, object?> data && data.TryGetValue(key, out var actual) && Equals(expected, actual);
    private sealed class Fixture
    {
        public Guid Unit { get; } = Guid.CreateVersion7(); public Guid Leader { get; } = Guid.CreateVersion7(); public Guid Actor { get; } = Guid.CreateVersion7();
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>(); public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ITenantMemberSelectionDirectory Members { get; } = Substitute.For<ITenantMemberSelectionDirectory>();
        public Transaction Transaction { get; } = new(); public bool MembersQueriedInsideTransaction { get; private set; }
        public OrganizationUnitLeadershipService Service { get; }
        public Fixture(DatabaseProvider provider = DatabaseProvider.SqlServer)
        {
            var tenantId = Guid.CreateVersion7(); var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(false); tenant.IsAvailable.Returns(true); tenant.Id.Returns(tenantId);
            var scope = Substitute.For<IUserDataScopeResolver>(); scope.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(true, []));
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>(); filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), Arg.Any<string>(), Arg.Any<Guid>()).Returns((DataScopeSqlFilter?)null);
            var now = DateTimeOffset.UtcNow; var options = Options.Create(new DatabaseOptions { Provider = provider }); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            Query.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(OrganizationSql.FindUnitById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new OrganizationUnitRecord(Unit, tenantId, null, "unit", "Unit", 1, true, now, null, 3));
            Query.QuerySingleOrDefaultAsync<OrganizationLeadershipUnitRecord>(provider == DatabaseProvider.SqlServer ? OrganizationUnitLeadershipSql.LockUnitSqlServer : OrganizationUnitLeadershipSql.LockUnitMySql,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new OrganizationLeadershipUnitRecord(Unit, true, 3));
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ => {
                MembersQueriedInsideTransaction |= Transaction.Inside; return new Dictionary<Guid, TenantUserDirectoryEntry> { [Leader] = new(Leader, "leader", "Leader") };
            });
            Service = new(Query, Commands, Transaction, new TenantUnitQueryService(Query, scope, filters, options), Members, tenant, clock, options);
        }
    }
    private sealed class Transaction : ICommandTransaction
    {
        public int Calls { get; private set; } public bool Inside { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        { Calls++; Inside = true; try { return await action(token); } finally { Inside = false; } }
    }
}
