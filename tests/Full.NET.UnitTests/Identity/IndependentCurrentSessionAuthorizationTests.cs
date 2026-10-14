using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>跨模块提交前复核使用独立数据状态及上下文副本，禁止加入调用方的本地事务。</summary>
[TestClass]
public sealed class IndependentCurrentSessionAuthorizationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Independent_scope_has_no_parent_transaction_and_copies_trusted_scope(bool isTenant)
    {
        var current = new CurrentTenantAccessor(); var id = isTenant ? Guid.CreateVersion7() : (Guid?)null;
        if (id is { } tenantId) current.SetTenant(new(tenantId, "trusted", "可信租户")); else current.SetHost();
        var actor = new AuthorizedSessionActor(Guid.CreateVersion7(), id, Guid.CreateVersion7()); var called = 0;
        IDataTransactionState? parentState = null;
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<IDataTransactionState>(_ => Substitute.For<IDataTransactionState>());
        services.AddScoped<IDatabaseAuthorizationReadScope>(_ => Capacity());
        services.AddScoped<ICurrentSessionAuthorization>(sp =>
        {
            var authorization = Substitute.For<ICurrentSessionAuthorization>();
            authorization.AuthorizeAsync("ai.agent_runs.reconcile", Arg.Any<CancellationToken>()).Returns(_ =>
            {
                called++; var copy = sp.GetRequiredService<ICurrentTenant>();
                Assert.AreNotSame(current, copy); Assert.AreEqual(id, copy.Id); Assert.AreEqual(!isTenant, copy.IsHost);
                Assert.IsFalse(sp.GetRequiredService<IDataTransactionState>().HasTransaction);
                Assert.AreNotSame(parentState, sp.GetRequiredService<IDataTransactionState>());
                return actor;
            }); return authorization;
        });
        using var provider = services.BuildServiceProvider();
        using var parentScope = provider.CreateScope();
        parentState = parentScope.ServiceProvider.GetRequiredService<IDataTransactionState>(); parentState.HasTransaction.Returns(true);
        var boundary = new IndependentCurrentSessionAuthorization(provider.GetRequiredService<IServiceScopeFactory>(), current, Capacity());
        Assert.AreEqual(actor, await boundary.AuthorizeAsync("ai.agent_runs.reconcile")); Assert.AreEqual(1, called);
        Assert.AreEqual(id, current.Id); Assert.AreEqual(!isTenant, current.IsHost);
        Assert.IsTrue(parentState.HasTransaction);
    }

    [TestMethod]
    public async Task Context_change_during_independent_read_denies_result_and_keeps_parent_context()
    {
        var current = new CurrentTenantAccessor(); current.SetHost(); var authorization = Substitute.For<ICurrentSessionAuthorization>();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { current.Clear(); return new AuthorizedSessionActor(Guid.CreateVersion7(), null, Guid.CreateVersion7()); });
        var services = new ServiceCollection(); services.AddScoped<ICurrentTenantContextWriter>(_ => new CurrentTenantAccessor());
        services.AddScoped<IDataTransactionState>(_ => Substitute.For<IDataTransactionState>()); services.AddScoped(_ => authorization);
        services.AddScoped<IDatabaseAuthorizationReadScope>(_ => Capacity());
        using var provider = services.BuildServiceProvider();
        var boundary = new IndependentCurrentSessionAuthorization(provider.GetRequiredService<IServiceScopeFactory>(), current, Capacity());
        Assert.IsNull(await boundary.AuthorizeAsync("ai.agent_runs.reconcile")); Assert.IsFalse(current.IsAvailable);
    }

    [TestMethod]
    public async Task Unexpected_transaction_in_independent_scope_rejects_before_authority()
    {
        var current = new CurrentTenantAccessor(); current.SetHost(); var services = new ServiceCollection();
        var state = Substitute.For<IDataTransactionState>(); state.HasTransaction.Returns(true); services.AddSingleton(state);
        using var provider = services.BuildServiceProvider();
        var boundary = new IndependentCurrentSessionAuthorization(provider.GetRequiredService<IServiceScopeFactory>(), current, Capacity());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => boundary.AuthorizeAsync("ai.agent_runs.reconcile"));
    }
    private static IDatabaseAuthorizationReadScope Capacity()
    {
        var capacity = Substitute.For<IDatabaseAuthorizationReadScope>(); capacity.HasIndependentReadCapacity.Returns(true);
        capacity.Enter().Returns(Substitute.For<IDisposable>()); return capacity;
    }
}
