using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>申请单本人范围使用可信创建者，不能扩大为同机构全部记录。</summary>
[TestClass]
public sealed class EnterpriseRequestReadScopeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Self_scope_uses_creator_for_list_count_and_detail(DatabaseProvider provider, bool detail)
    {
        var f = new Fixture(provider, new(false, [new(Guid.NewGuid(), RoleDataScopeKinds.Self)]));
        await f.Read(detail);
        foreach (var call in f.Queries.Calls)
        {
            StringAssert.Contains(call.Sql.Text, "CreatedById = @EnterpriseRequestSelfUserId");
            Assert.IsFalse(call.Sql.Text.Contains("@SelfUnit", StringComparison.Ordinal));
            Assert.AreEqual(f.Actor, call.Parameters["EnterpriseRequestSelfUserId"]);
            Assert.AreEqual(SqlDataScope.TenantRequired, call.Sql.Scope);
            Assert.AreEqual(SqlTenantBinding.CurrentTenantId, call.Sql.TenantBinding);
        }
        Assert.AreEqual(detail ? 1 : 2, f.Queries.Calls.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Self_and_department_roles_preserve_union_without_self_unit_leak(bool detail)
    {
        var f = new Fixture(DatabaseProvider.SqlServer, new(false,
            [new(Guid.NewGuid(), RoleDataScopeKinds.Self), new(Guid.NewGuid(), RoleDataScopeKinds.Organization)]));
        await f.Read(detail);
        foreach (var call in f.Queries.Calls)
        {
            StringAssert.Contains(call.Sql.Text, "CreatedById = @EnterpriseRequestSelfUserId");
            StringAssert.Contains(call.Sql.Text, "OrganizationUnitId = @SelectedUnit");
            StringAssert.Contains(call.Sql.Text, " OR ");
            Assert.IsFalse(call.Sql.Text.Contains("@SelfUnit", StringComparison.Ordinal));
            Assert.AreEqual(f.Unit, call.Parameters["SelectedUnit"]);
        }
    }

    [TestMethod]
    [DataRow("all")]
    [DataRow("empty")]
    [DataRow("department")]
    public async Task Other_scopes_keep_existing_port_semantics(string kind)
    {
        var scope = kind switch
        {
            "all" => new EffectiveUserDataScope(true, []),
            "department" => new(false, [new(Guid.NewGuid(), RoleDataScopeKinds.Organization)]),
            _ => new(false, []),
        };
        var f = new Fixture(DatabaseProvider.MySql, scope);
        await f.Read(false);
        foreach (var call in f.Queries.Calls)
        {
            Assert.IsFalse(call.Sql.Text.Contains("EnterpriseRequestSelfUserId", StringComparison.Ordinal));
            if (kind == "department") StringAssert.Contains(call.Sql.Text, "OrganizationUnitId = @SelectedUnit");
            if (kind == "empty") StringAssert.Contains(call.Sql.Text, "1 = 0");
            if (kind == "all") Assert.IsFalse(call.Sql.Name.EndsWith(".data_scope", StringComparison.Ordinal));
        }
    }

    private sealed class Fixture
    {
        internal readonly Guid Actor = Guid.NewGuid(), Unit = Guid.NewGuid();
        internal readonly RecordingQueries Queries = new();
        private readonly EnterpriseRequestQueryService service;

        internal Fixture(DatabaseProvider provider, EffectiveUserDataScope scope)
        {
            var resolver = Substitute.For<IUserDataScopeResolver>();
            resolver.ResolveAsync(Actor, false, Arg.Any<CancellationToken>()).Returns(scope);
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>();
            filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", Actor)
                .Returns(call =>
                {
                    var value = call.ArgAt<EffectiveUserDataScope>(0);
                    if (value.IsUnrestricted) return null;
                    if (value.RoleScopes.Count == 0) return new DataScopeSqlFilter("1 = 0", null);
                    var clauses = value.RoleScopes.Select(role => role.DataScopeKind == RoleDataScopeKinds.Self
                        ? "OrganizationUnitId = @SelfUnit" : "OrganizationUnitId = @SelectedUnit");
                    return new DataScopeSqlFilter(string.Join(" OR ", clauses), new Dictionary<string, object?>
                        { ["SelfUnit"] = Guid.NewGuid(), ["SelectedUnit"] = Unit });
                });
            service = new(Queries, Options.Create(new DatabaseOptions { Provider = provider }), resolver, filters);
        }

        internal async Task Read(bool detail)
        {
            if (detail) await service.GetByIdAsync(Guid.NewGuid(), Actor, false);
            else await service.ListAsync(Actor, false, 1, 20);
        }
    }

    private sealed class RecordingQueries : IQueryExecutor
    {
        internal readonly List<(SqlStatement Sql, IReadOnlyDictionary<string, object?> Parameters)> Calls = [];
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Record(statement, parameters);
            return Task.FromResult(default(T));
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Record(statement, parameters);
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
        private void Record(SqlStatement statement, object? parameters) => Calls.Add((statement,
            parameters is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary() : new Dictionary<string, object?>()));
    }
}
