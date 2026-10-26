using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Workflow;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>已选角色交集不截断名册，分页在 Identity 权威关系中执行，租户只能来自可信上下文。</summary>
[TestClass]
public sealed class WorkflowRoleSelectionDirectoryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    public async Task Empty_active_role_is_distinguished_from_missing_or_foreign_role(DatabaseProvider provider, bool host)
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = Tenant(host);
        var active = Guid.CreateVersion7(); var missing = Guid.CreateVersion7();
        var roles = host ? WorkflowRoleMemberSql.FindActiveHostRolesByIds : WorkflowRoleMemberSql.FindActiveTenantRolesByIds;
        query.QueryAsync<WorkflowRoleListRow>(roles, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowRoleListRow(active, "empty", "Empty") });
        var members = host
            ? provider == DatabaseProvider.MySql ? WorkflowRoleMemberSql.ListActiveHostRoleMembersByRoleIdsMySql : WorkflowRoleMemberSql.ListActiveHostRoleMembersByRoleIds
            : provider == DatabaseProvider.MySql ? WorkflowRoleMemberSql.ListActiveTenantRoleMembersByRoleIdsMySql : WorkflowRoleMemberSql.ListActiveTenantRoleMembersByRoleIds;
        query.QueryAsync<WorkflowRoleMemberRow>(members, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowRoleMemberRow>());
        var directory = new WorkflowRoleMemberDirectory(query, Options.Create(new DatabaseOptions { Provider = provider }), tenant);
        var result = await directory.FindActiveMemberUserIdsByRoleIdsAsync([active, missing], 21);
        Assert.IsTrue(result.ContainsKey(active)); Assert.HasCount(0, result[active]); Assert.IsFalse(result.ContainsKey(missing));
        await query.Received(1).QueryAsync<WorkflowRoleMemberRow>(members,
            Arg.Is<object?>(value => HasIds(value, "RoleIds", new[] { active }) && (host || Has(value, "TenantId", tenant.Id))), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Selected_intersection_uses_one_scope_bound_query(bool host)
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = Tenant(host); var role = Guid.CreateVersion7(); var users = Enumerable.Range(0, 30).Select(_ => Guid.CreateVersion7()).ToArray();
        var statement = host ? WorkflowRoleMemberSql.FindSelectedHostRoleMembers : WorkflowRoleMemberSql.FindSelectedTenantRoleMembers;
        query.QueryAsync<WorkflowRoleMemberRow>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { new WorkflowRoleMemberRow(role, users[^1]) });
        var directory = new WorkflowRoleMemberDirectory(query, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), tenant);
        var result = await directory.FindSelectedActiveMembersByRoleIdsAsync([role], users);
        CollectionAssert.AreEqual(new[] { users[^1] }, result[role].ToArray()); Assert.AreEqual(1, query.ReceivedCalls().Count());
        await query.Received(1).QueryAsync<WorkflowRoleMemberRow>(statement,
            Arg.Is<object?>(value => HasIds(value, "UserIds", users) && (host || Has(value, "TenantId", tenant.Id))), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    public async Task Role_member_pagination_reads_the_actual_role_union(DatabaseProvider provider, bool host)
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = Tenant(host); var role = Guid.CreateVersion7();
        var count = host ? WorkflowRoleSelectionSql.CountHost : WorkflowRoleSelectionSql.CountTenant;
        var list = host ? provider == DatabaseProvider.SqlServer ? WorkflowRoleSelectionSql.ListHostSqlServer : WorkflowRoleSelectionSql.ListHostMySql
            : provider == DatabaseProvider.SqlServer ? WorkflowRoleSelectionSql.ListTenantSqlServer : WorkflowRoleSelectionSql.ListTenantMySql;
        query.QuerySingleOrDefaultAsync<long>(count, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(21);
        query.QueryAsync<WorkflowRoleUserRow>(list, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { new WorkflowRoleUserRow(Guid.CreateVersion7(), "last", "Last") });
        var directory = new WorkflowRoleMemberDirectory(query, Options.Create(new DatabaseOptions { Provider = provider }), tenant);
        var result = await directory.ListActiveMembersOfRolesAsync([role], 2, 20);
        Assert.AreEqual(21L, result.Total); Assert.AreEqual(1, result.Items.Count);
        await query.Received(1).QueryAsync<WorkflowRoleUserRow>(list, Arg.Is<object?>(value => Has(value, "Offset", 20L)), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Missing_tenant_or_oversized_selection_fails_before_querying()
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(false);
        var directory = new WorkflowRoleMemberDirectory(query, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), tenant);
        await Assert.ThrowsExactlyAsync<TenantContextMissingException>(() => directory.FindSelectedActiveMembersByRoleIdsAsync([Guid.CreateVersion7()], [Guid.CreateVersion7()]));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => directory.FindSelectedActiveMembersByRoleIdsAsync([Guid.CreateVersion7()], Enumerable.Range(0, 161).Select(_ => Guid.CreateVersion7()).ToArray()));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }
    private static ICurrentTenant Tenant(bool host)
    {
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(host); tenant.IsAvailable.Returns(!host);
        if (!host) tenant.Id.Returns(Guid.CreateVersion7()); return tenant;
    }
    private static bool Has(object? value, string name, object? expected) => value is IReadOnlyDictionary<string, object?> parameters && parameters.TryGetValue(name, out var actual) && Equals(actual, expected);
    private static bool HasIds(object? value, string name, Guid[] expected) => value is IReadOnlyDictionary<string, object?> parameters && parameters.TryGetValue(name, out var actual) && actual is Guid[] ids && ids.SequenceEqual(expected);
}
