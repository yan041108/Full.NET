using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>应用后台文档必须复用真实数据范围，不通过 HTTP 注册或超管声明补齐依赖。</summary>
[TestClass]
public sealed class EnterpriseRequestBackgroundDataScopeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Scoped_list_composes_count_and_page_with_the_same_tenant_boundary(DatabaseProvider database)
    {
        var query = new CapturingQueryExecutor(); var resolver = Substitute.For<IUserDataScopeResolver>();
        var filters = Substitute.For<IDataScopeSqlFilterBuilder>(); var actor = Guid.CreateVersion7(); var unit = Guid.CreateVersion7();
        resolver.ResolveAsync(actor, false, Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
        filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", actor)
            .Returns(new DataScopeSqlFilter("OrganizationUnitId = @AllowedUnitId", new Dictionary<string, object?> { ["AllowedUnitId"] = unit }));
        var service = new EnterpriseRequestQueryService(query, Options.Create(new DatabaseOptions { Provider = database }), resolver, filters);
        var result = await service.ListAsync(actor, false, 1, 20); Assert.IsTrue(result.IsSuccess); Assert.HasCount(2, query.Statements);
        foreach (var statement in query.Statements)
            StringAssert.Contains(statement.Text, "AND TenantId = @TenantId AND (OrganizationUnitId = @AllowedUnitId)");
        foreach (var values in query.Parameters)
        {
            var dictionary = (IDictionary<string, object?>)values!;
            Assert.AreEqual(unit, dictionary["AllowedUnitId"]); Assert.AreEqual(20, dictionary["PageSize"]);
        }
    }

    [TestMethod]
    public async Task Worker_resolves_authoritative_organization_scope_without_http_authorization()
    {
        var services = new ServiceCollection(); var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton(Substitute.For<IQueryExecutor>());
        new IdentityModule().AddBackgroundServices(services, configuration);
        new OrganizationModule().AddBackgroundServices(services, configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IUserDataScopeResolver>());
        var builder = scope.ServiceProvider.GetRequiredService<IDataScopeSqlFilterBuilder>();
        var filter = builder.BuildOrganizationUnitFilter(new EffectiveUserDataScope(false,
            [new RoleDataScopeEntry(Guid.CreateVersion7(), RoleDataScopeKinds.Organization)]), "request.OrganizationUnitId", Guid.CreateVersion7());
        Assert.IsNotNull(filter); StringAssert.Contains(filter.Sql, "request.OrganizationUnitId");
        Assert.IsNotNull(filter.Parameters); StringAssert.Contains(filter.Sql, "@DataScopeUserId");
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(IAuthorizationService)));
    }

    [TestMethod]
    public async Task Worker_registration_preserves_explicit_data_scope_overrides_and_is_idempotent()
    {
        var services = new ServiceCollection(); var configuration = new ConfigurationBuilder().Build();
        var resolver = Substitute.For<IUserDataScopeResolver>(); var filter = Substitute.For<IDataScopeSqlFilterBuilder>();
        services.AddSingleton(resolver); services.AddSingleton(filter);
        var module = new IdentityModule(); module.AddBackgroundServices(services, configuration); module.AddBackgroundServices(services, configuration);
        await using var provider = services.BuildServiceProvider();
        Assert.AreSame(resolver, provider.GetRequiredService<IUserDataScopeResolver>());
        Assert.AreSame(filter, provider.GetRequiredService<IDataScopeSqlFilterBuilder>());
        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(IUserDataScopeResolver)));
        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(IDataScopeSqlFilterBuilder)));
    }

    // 记录内部生成模型的查询边界，不为动态代理放宽生产程序集的可见性。
    private sealed class CapturingQueryExecutor : IQueryExecutor
    {
        internal List<SqlStatement> Statements { get; } = [];
        internal List<object?> Parameters { get; } = [];
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Statements.Add(statement); Parameters.Add(parameters); return Task.FromResult<T?>(default);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Statements.Add(statement); Parameters.Add(parameters); return Task.FromResult<IReadOnlyList<T>>([]);
        }
    }
}
