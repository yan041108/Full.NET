using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions.Queries;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits.Queries;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>列表使用静态 JOIN，详情继续通过用户契约回读；覆盖参数、取消与范围并集。</summary>
[TestClass]
public sealed class OrganizationHostUserDirectoryCompositionTests
{
    private static readonly Guid Actor = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf291");
    private static readonly Guid Unit = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf292");

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task User_unit_list_uses_database_total_and_same_escaped_filters_before_paging(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>();
        var resolver = UnrestrictedResolver();
        var scope = new TenantUserUnitListScopeReader(query, resolver, Substitute.For<IDataScopeSqlFilterBuilder>(), Options(provider));
        var row = new OrganizationUserUnitResponse(Guid.NewGuid(), Actor, "disabled-user", "停用用户", Unit, "code", "机构", false, false, DateTimeOffset.UtcNow, null, 1);
        query.QuerySingleOrDefaultAsync<long>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(21L);
        query.QueryAsync<OrganizationUserUnitResponse>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new[] { row });
        var service = new TenantUserUnitListQueryService(query, scope, Options(provider));

        var result = await service.ListAsync(Actor, false, 2, 20, Actor, Unit, " % ", "r_", false);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(21L, result.Value!.Total);
        Assert.AreEqual(row, result.Value.Items.Single());
        var calls = query.ReceivedCalls().ToArray();
        Assert.HasCount(2, calls);
        var parameters = (IReadOnlyDictionary<string, object?>)calls[0].GetArguments()[1]!;
        Assert.AreSame(parameters, calls[1].GetArguments()[1]);
        Assert.AreEqual("%!%%", parameters["UserSearch"]);
        Assert.AreEqual("%r!_%", parameters["UnitSearch"]);
        Assert.AreEqual(false, parameters["IsActive"]);
        Assert.AreEqual(20L, parameters["Offset"]);
        Assert.AreEqual(Actor, parameters["UserId"]);
        Assert.AreEqual(Unit, parameters["UnitId"]);
        Assert.AreEqual(true, parameters["Unrestricted"]);
        StringAssert.EndsWith(((SqlStatement)calls[1].GetArguments()[0]!).Name, provider == DatabaseProvider.MySql ? ".mysql" : ".sql_server");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task User_position_list_clamps_page_size_and_avoids_offset_overflow(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>();
        var service = new TenantUserPositionListQueryService(query, Options(provider));
        query.QueryAsync<OrganizationUserPositionResponse>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<OrganizationUserPositionResponse>());

        var result = await service.ListAsync(int.MaxValue, 500, null, Unit, " ", "职位", true);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(100, result.Value!.PageSize);
        var calls = query.ReceivedCalls().ToArray();
        Assert.HasCount(2, calls);
        var parameters = (IReadOnlyDictionary<string, object?>)calls[0].GetArguments()[1]!;
        Assert.AreSame(parameters, calls[1].GetArguments()[1]);
        Assert.AreEqual(((long)int.MaxValue - 1) * 100, parameters["Offset"]);
        Assert.IsNull(parameters["UserSearch"]);
        Assert.AreEqual("%职位%", parameters["PositionSearch"]);
        Assert.AreEqual(true, parameters["IsActive"]);
    }

    [TestMethod]
    public async Task Both_lists_reject_unbounded_search_without_querying_database()
    {
        var query = Substitute.For<IQueryExecutor>();
        var resolver = UnrestrictedResolver();
        var scope = new TenantUserUnitListScopeReader(query, resolver, Substitute.For<IDataScopeSqlFilterBuilder>(), Options(DatabaseProvider.SqlServer));
        var units = new TenantUserUnitListQueryService(query, scope, Options(DatabaseProvider.SqlServer));
        var positions = new TenantUserPositionListQueryService(query, Options(DatabaseProvider.SqlServer));
        Assert.IsFalse((await units.ListAsync(Actor, false, 1, 20, null, null, new string('x', 101))).IsSuccess);
        Assert.IsFalse((await positions.ListAsync(1, 20, null, null, positionSearch: new string('x', 101))).IsSuccess);
        Assert.IsEmpty(query.ReceivedCalls());
        Assert.IsEmpty(resolver.ReceivedCalls());
    }

    [TestMethod]
    public async Task Restricted_unit_scope_is_passed_as_json_and_empty_scope_denies_all()
    {
        var query = Substitute.For<IQueryExecutor>();
        var resolver = Substitute.For<IUserDataScopeResolver>();
        resolver.ResolveAsync(Actor, false, Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
        var scope = new TenantUserUnitListScopeReader(query, resolver, Substitute.For<IDataScopeSqlFilterBuilder>(), Options(DatabaseProvider.SqlServer));
        query.QueryAsync<OrganizationUserUnitResponse>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<OrganizationUserUnitResponse>());
        var service = new TenantUserUnitListQueryService(query, scope, Options(DatabaseProvider.SqlServer));

        await service.ListAsync(Actor, false, 1, 20, null, null);

        var parameters = (IReadOnlyDictionary<string, object?>)query.ReceivedCalls().First().GetArguments()[1]!;
        Assert.AreEqual(false, parameters["Unrestricted"]);
        Assert.AreEqual("[]", parameters["VisibleUnitIds"]);
        Assert.HasCount(2, query.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Unit_scope_preserves_custom_and_subtree_union_without_dynamic_join_sql(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>();
        var resolver = Substitute.For<IUserDataScopeResolver>();
        var builder = Substitute.For<IDataScopeSqlFilterBuilder>();
        var other = Guid.NewGuid();
        resolver.ResolveAsync(Actor, false, Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false,
            [new(Guid.NewGuid(), RoleDataScopeKinds.Custom), new(Guid.NewGuid(), RoleDataScopeKinds.OrganizationSubtree)]));
        builder.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "Id", Actor).Returns(new DataScopeSqlFilter("Id = @AllowedId", new Dictionary<string, object?> { ["AllowedId"] = Unit }));
        query.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(
            info => info[0] is SqlStatement statement && statement.Name.Contains("subtree", StringComparison.Ordinal) ? new[] { Unit, other } : new[] { Unit });
        var scope = new TenantUserUnitListScopeReader(query, resolver, builder, Options(provider));

        var ids = await scope.ReadAsync(Actor, false, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { Unit, other }, ids!);
        builder.Received(1).BuildOrganizationUnitFilter(Arg.Is<EffectiveUserDataScope>(s => s != null && s.RoleScopes.Count == 1 && s.RoleScopes[0].DataScopeKind == RoleDataScopeKinds.Custom), "Id", Actor);
        Assert.HasCount(2, query.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Cancellation_reaches_authoritative_scope_resolver_and_query_executor()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = Substitute.For<IQueryExecutor>();
        var resolver = Substitute.For<IUserDataScopeResolver>();
        resolver.ResolveAsync(Actor, false, cancellation.Token).Returns(Task.FromCanceled<EffectiveUserDataScope>(cancellation.Token));
        var scope = new TenantUserUnitListScopeReader(query, resolver, Substitute.For<IDataScopeSqlFilterBuilder>(), Options(DatabaseProvider.SqlServer));
        var units = new TenantUserUnitListQueryService(query, scope, Options(DatabaseProvider.SqlServer));
        await Assert.ThrowsAsync<OperationCanceledException>(() => units.ListAsync(Actor, false, 1, 20, null, null, cancellationToken: cancellation.Token));
        var positions = new TenantUserPositionListQueryService(query, Options(DatabaseProvider.SqlServer));
        query.QuerySingleOrDefaultAsync<long>(Arg.Any<SqlStatement>(), Arg.Any<object>(), cancellation.Token).Returns(Task.FromCanceled<long>(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => positions.ListAsync(1, 20, null, null, cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task Write_side_details_continue_to_resolve_display_through_contract()
    {
        var query = Substitute.For<IQueryExecutor>();
        var directory = Substitute.For<IHostUserDisplayDirectory>();
        query.QuerySingleOrDefaultAsync<OrganizationUserUnitListRow>(OrganizationSql.FindUserUnitById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new OrganizationUserUnitListRow { Id = Unit, UserId = Actor });
        query.QuerySingleOrDefaultAsync<OrganizationUserPositionListRow>(OrganizationSql.FindUserPositionById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new OrganizationUserPositionListRow { Id = Unit, UserId = Actor });
        directory.FindHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [Actor] = new(Actor, "user", "用户") });

        Assert.IsTrue((await new TenantUserUnitQueryService(query, directory).GetByIdAsync(Unit)).IsSuccess);
        Assert.IsTrue((await new TenantUserPositionQueryService(query, directory).GetByIdAsync(Unit)).IsSuccess);
        await directory.Received(2).FindHostUsersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(Actor)), Arg.Any<CancellationToken>());
    }

    private static IOptions<DatabaseOptions> Options(DatabaseProvider provider) => Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Provider = provider });

    private static IUserDataScopeResolver UnrestrictedResolver()
    {
        var resolver = Substitute.For<IUserDataScopeResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(true, []));
        return resolver;
    }
}
