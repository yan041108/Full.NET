using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageReportingLines;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>直属汇报权限、身份准备、串行门闩、乐观修订和有界环检测的定向规则。</summary>
[TestClass]
public sealed class OrganizationReportingLineTests
{
    [TestMethod]
    [DataRow("GET", "", OrganizationReportingLinePermissions.Read)]
    [DataRow("PUT", "", OrganizationReportingLinePermissions.Update)]
    [DataRow("GET", "/candidates", OrganizationReportingLinePermissions.Update)]
    public async Task Endpoints_declare_separate_exact_permissions(string method, string suffix, string permission)
    {
        var builder = WebApplication.CreateBuilder(); new OrganizationModule().AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>()); await using var app = builder.Build();
        Full.NET.Modules.Organization.Features.ManageReportingLines.Endpoint.Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText == "/api/v1/organization/users/{employeeUserId:guid}/reporting-line" + suffix && value.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(); Assert.HasCount(1, policies); Assert.AreEqual(FullNetPermissionPolicies.For(permission), policies[0].Policy);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Assignment_prepares_both_identities_before_locking_owned_graph(DatabaseProvider provider)
    {
        var f = new Fixture(provider); var result = await f.Service.SetAsync(f.Employee, f.Actor, false, new(f.Manager, 0), default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1L, result.Value!.Revision); Assert.AreEqual(f.Actor, result.Value.UpdatedById); Assert.IsFalse(f.IdentityInside);
        await f.Commands.Received(1).ExecuteAsync(provider == DatabaseProvider.SqlServer ? OrganizationReportingLineSql.EnsureGuardSqlServer : OrganizationReportingLineSql.EnsureGuardMySql, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(OrganizationReportingLineSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Restricted_data_scope_is_rejected_before_identity_or_graph_reads()
    {
        var f = new Fixture(); f.Scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
        var result = await f.Service.SetAsync(f.Employee, f.Actor, false, new(f.Manager, 0), default);
        Assert.AreEqual(OrganizationErrorCodes.ReportingLineScopeDenied, result.Error!.Code); Assert.AreEqual(0, f.Members.ReceivedCalls().Count()); Assert.AreEqual(0, f.Transaction.Calls);
    }

    [TestMethod]
    public async Task Inactive_or_foreign_manager_never_opens_transaction()
    {
        var f = new Fixture(); f.Active.Remove(f.Manager);
        var result = await f.Service.SetAsync(f.Employee, f.Actor, false, new(f.Manager, 0), default);
        Assert.AreEqual(OrganizationErrorCodes.ReportingLineInvalid, result.Error!.Code); Assert.AreEqual(0, f.Transaction.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cycle_or_excessive_depth_is_rejected_under_graph_lock(bool depth)
    {
        var f = new Fixture(); var current = f.Manager;
        for (var index = 0; index < (depth ? 64 : 1); index++)
        {
            var next = depth ? Guid.CreateVersion7() : f.Employee; f.Rows[current] = f.Row(current, next); current = next;
        }
        var result = await f.Service.SetAsync(f.Employee, f.Actor, false, new(f.Manager, 0), default);
        Assert.AreEqual(OrganizationErrorCodes.ReportingLineCycle, result.Error!.Code); Assert.AreEqual(1, f.Transaction.Calls);
        await f.Commands.DidNotReceive().ExecuteAsync(OrganizationReportingLineSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Stale_revision_cannot_overwrite_and_explicit_clear_can_repair_relationship()
    {
        var f = new Fixture(); f.Rows[f.Employee] = f.Row(f.Employee, f.Manager);
        var stale = await f.Service.SetAsync(f.Employee, f.Actor, false, new(null, 0), default);
        Assert.AreEqual(OrganizationErrorCodes.ReportingLineConflict, stale.Error!.Code);
        var clear = await f.Service.SetAsync(f.Employee, f.Actor, false, new(null, 1), default);
        Assert.IsTrue(clear.IsSuccess); Assert.IsNull(clear.Value!.ManagerUserId); Assert.AreEqual(2L, clear.Value.Revision);
    }

    private sealed class Fixture
    {
        public Guid Employee { get; } = Guid.CreateVersion7(); public Guid Manager { get; } = Guid.CreateVersion7(); public Guid Actor { get; } = Guid.CreateVersion7();
        public Dictionary<Guid, OrganizationReportingLineRecord> Rows { get; } = []; public Dictionary<Guid, TenantUserDirectoryEntry> Active { get; } = [];
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>(); public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ITenantMemberSelectionDirectory Members { get; } = Substitute.For<ITenantMemberSelectionDirectory>(); public IUserDataScopeResolver Scopes { get; } = Substitute.For<IUserDataScopeResolver>();
        public Transaction Transaction { get; } = new(); public bool IdentityInside { get; private set; } public OrganizationReportingLineService Service { get; }
        public OrganizationReportingLineRecord Row(Guid employee, Guid? manager) => new(Guid.CreateVersion7(), employee, manager, 1, Actor, DateTimeOffset.UtcNow);
        public Fixture(DatabaseProvider provider = DatabaseProvider.SqlServer)
        {
            var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(false); tenant.IsAvailable.Returns(true); tenant.Id.Returns(Guid.CreateVersion7());
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            Scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(true, []));
            Active[Employee] = new(Employee, "employee", "Employee"); Active[Manager] = new(Manager, "manager", "Manager");
            Members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => {
                IdentityInside |= Transaction.Inside; return Active.Where(pair => call.ArgAt<IReadOnlyCollection<Guid>>(0).Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            });
            Query.QuerySingleOrDefaultAsync<OrganizationReportingLineRecord>(OrganizationReportingLineSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
                Rows.GetValueOrDefault((Guid)((IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1))["EmployeeUserId"]!));
            Query.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Guid.CreateVersion7());
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Service = new(Query, Commands, Transaction, Members, Scopes, tenant, clock, ids, Options.Create(new DatabaseOptions { Provider = provider }));
        }
    }
    private sealed class Transaction : ICommandTransaction
    {
        public int Calls { get; private set; } public bool Inside { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        { Calls++; Inside = true; try { return await action(token); } finally { Inside = false; } }
    }
}
