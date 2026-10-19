using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>租户解析 C0 权威读：源故障传播、停用后不得复用旧活动状态，且无缓存回填窗口。</summary>
[TestClass]
public sealed class TenantResolverTests
{
    [TestMethod]
    [DataRow("active")]
    [DataRow("domain")]
    public async Task Authority_failure_after_success_never_returns_previous_active_state(string path)
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        var failure = new InvalidOperationException("source unavailable");
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                path == "domain" ? TenantSql.FindByDomain : TenantSql.FindById,
                Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<TenantResolutionRecord?>(ToRecord(expected)),
                _ => Task.FromException<TenantResolutionRecord?>(failure));
        var resolver = new TenantResolver(executor);
        async Task<object?> ReadAsync() => path == "domain"
            ? await resolver.ResolveByDomainAsync(expected.Domain!)
            : await resolver.ResolveActiveByIdAsync(expected.Id);
        Assert.IsNotNull(await ReadAsync());
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ReadAsync());
        Assert.AreSame(failure, error, "必须传播本次权威查询的原始异常，不能以其他选项异常代替安全失败。");
        await executor.Received(2).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            path == "domain" ? TenantSql.FindByDomain : TenantSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("id", true)]
    [DataRow("active", true)]
    [DataRow("domain", true)]
    [DataRow("id", false)]
    [DataRow("active", false)]
    [DataRow("domain", false)]
    public async Task Cancelled_authority_read_never_reuses_previous_success(string path, bool cancelBeforeRead)
    {
        var expected = CreateSummary();
        var statement = path == "domain" ? TenantSql.FindByDomain : TenantSql.FindById;
        var executor = Substitute.For<IQueryExecutor>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TenantResolutionRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<CancellationToken>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(statement, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                observed.Add(token);
                if (observed.Count == 1) return Task.FromResult<TenantResolutionRecord?>(ToRecord(expected));
                started.TrySetResult();
                return release.Task.WaitAsync(token);
            });
        var resolver = new TenantResolver(executor);
        async Task<object?> ReadAsync(CancellationToken token) => path switch
        {
            "id" => await resolver.ResolveByIdAsync(expected.Id, token),
            "active" => await resolver.ResolveActiveByIdAsync(expected.Id, token),
            _ => await resolver.ResolveByDomainAsync(expected.Domain!, token),
        };
        Assert.IsNotNull(await ReadAsync(CancellationToken.None));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new CancellationTokenSource();
        Task<object?>? pending = null;
        try
        {
            if (cancelBeforeRead) request.Cancel();
            pending = ReadAsync(request.Token);
            if (!cancelBeforeRead)
            {
                // gate 证明权威查询已经进入，再取消请求；不以 sleep 推断取消发生时机。
                await started.Task.WaitAsync(deadline.Token);
                request.Cancel();
            }
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(deadline.Token));
            Assert.AreEqual(request.Token, error.CancellationToken, "不能把请求取消换成外层测试预算取消。");
            Assert.AreEqual(cancelBeforeRead ? 1 : 2, observed.Count);
            if (!cancelBeforeRead) Assert.AreEqual(request.Token, observed[1]);
        }
        finally
        {
            request.Cancel();
            release.TrySetResult(null);
            if (pending is not null)
            {
                try { await pending; } catch (OperationCanceledException) { }
            }
        }
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("domain")]
    public async Task Authority_state_changes_remain_isolated_between_tenants(string path)
    {
        var first = CreateSummary();
        var second = CreateSummary() with { Identifier = "beta", Name = "Beta", Domain = "beta.localhost" };
        var currentFirst = first;
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var statement = call.Arg<SqlStatement>();
                var parameters = call.ArgAt<object>(1);
                TenantSummary? selected = statement == TenantSql.FindByDomain
                    ? ReadDomain(parameters) switch { "acme.localhost" => currentFirst, "beta.localhost" => second, _ => null }
                    : ReadSqlParameter<Guid>(parameters, "TenantId") switch
                    {
                        var id when id == first.Id => currentFirst,
                        var id when id == second.Id => second,
                        _ => null,
                    };
                return Task.FromResult(selected is null ? null : ToRecord(selected));
            });
        var resolver = new TenantResolver(executor);
        async Task<TenantSummary?> ReadAsync(TenantSummary tenant) => path == "domain"
            ? await resolver.ResolveByDomainAsync(tenant.Domain!)
            : await resolver.ResolveByIdAsync(tenant.Id);
        Assert.AreEqual(first, await ReadAsync(first));
        Assert.AreEqual(second, await ReadAsync(second));
        currentFirst = first with { IsActive = false, Version = 2 };
        Assert.AreEqual(currentFirst, await ReadAsync(first));
        Assert.AreEqual(second, await ReadAsync(second));
        Assert.IsNull(await resolver.ResolveActiveByIdAsync(first.Id));
        Assert.AreEqual(second.Id, (await resolver.ResolveActiveByIdAsync(second.Id))?.Id);
        Assert.IsNull(await resolver.ResolveActiveByIdAsync(Guid.CreateVersion7()));
        Assert.IsNull(await resolver.ResolveByDomainAsync("unknown.localhost"));
    }

    [TestMethod]
    public async Task Late_pre_commit_read_cannot_poison_next_admission()
    {
        var expected = CreateSummary();
        var gate = new TaskCompletionSource<TenantResolutionRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task, Task.FromResult<TenantResolutionRecord?>(ToRecord(expected with { IsActive = false, Version = 2 })));
        var resolver = new TenantResolver(executor);
        var inFlight = resolver.ResolveActiveByIdAsync(expected.Id);
        // 此时模拟停用已提交；旧查询才返回，不能回填并影响提交后发起的准入。
        gate.SetResult(ToRecord(expected));
        Assert.IsNotNull(await inFlight);
        Assert.IsNull(await resolver.ResolveActiveByIdAsync(expected.Id));
    }

    [TestMethod]
    public async Task ResolveActiveByIdAsync_rechecks_authority_after_disable_without_invalidation()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ToRecord(expected), ToRecord(expected with { IsActive = false, Version = 2 }));
        await using var provider = CreateCacheProvider();
        var resolver = new TenantResolver(executor);

        Assert.IsNotNull(await resolver.ResolveActiveByIdAsync(expected.Id));
        Assert.IsNull(await resolver.ResolveActiveByIdAsync(expected.Id));
    }

    [TestMethod]
    public async Task ResolveByDomainAsync_does_not_accept_previous_domain_binding()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindByDomain, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ToRecord(expected), (TenantResolutionRecord?)null);
        await using var provider = CreateCacheProvider();
        var resolver = new TenantResolver(executor);

        Assert.IsNotNull(await resolver.ResolveByDomainAsync(expected.Domain!));
        Assert.IsNull(await resolver.ResolveByDomainAsync(expected.Domain!));
    }

    [TestMethod]
    public async Task ResolveByDomainAsync_NormalizesAndReadsActiveTenantFromAuthority()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantResolutionRecord?>(ToRecord(expected)));

        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(executor);

        var first = await resolver.ResolveByDomainAsync("  Acme.LocalHost  ");
        var second = await resolver.ResolveByDomainAsync("acme.localhost");

        Assert.AreEqual(expected, first);
        Assert.AreEqual(expected.Id, second?.Id);
        await executor.Received(2).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == "tenancy.find_by_domain"),
            Arg.Is<object>(parameters =>
                parameters != null && ReadDomain(parameters) == "acme.localhost"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveByDomainAsync_RechecksMissingTenantWithoutNegativeCache()
    {
        var executor = Substitute.For<IQueryExecutor>();
        executor
            .QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantResolutionRecord?>(null));

        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(executor);

        Assert.IsNull(await resolver.ResolveByDomainAsync("missing.localhost"));
        Assert.IsNull(await resolver.ResolveByDomainAsync("MISSING.LOCALHOST"));
        await executor.Received(2).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            Arg.Any<SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveByIdAsync_uses_the_explicit_global_id_query()
    {
        var expected = CreateSummary();
        var executor = Substitute.For<IQueryExecutor>();
        executor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(ToRecord(expected));
        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(executor);

        var result = await resolver.ResolveByIdAsync(expected.Id);

        Assert.AreEqual(expected, result);
        await executor.Received(1).QuerySingleOrDefaultAsync<TenantResolutionRecord>(
            TenantSql.FindById,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetAvailableAsync_returns_the_stably_ordered_global_query()
    {
        var executor = Substitute.For<IQueryExecutor>();
        executor.QueryAsync<TenantResolutionRecord>(
                TenantSql.GetAvailable,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<TenantResolutionRecord>>(
                Array.Empty<TenantResolutionRecord>()));
        await using var provider = CreateCacheProvider();
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var resolver = new TenantResolver(executor);

        var result = await resolver.GetAvailableAsync();

        Assert.HasCount(0, result);
        await executor.Received(1).QueryAsync<TenantResolutionRecord>(
            TenantSql.GetAvailable,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    private static TenantSummary CreateSummary() => new(
        Guid.CreateVersion7(),
        "acme",
        "Acme",
        "acme.localhost",
        true,
        1);

    private static TenantResolutionRecord ToRecord(TenantSummary summary) => new(
        summary.Id,
        summary.Identifier,
        summary.Name,
        summary.Domain,
        summary.IsActive,
        summary.Version,
        summary.DefaultLocale);

    private static ServiceProvider CreateCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFusionCache().AsHybridCache();
        return services.BuildServiceProvider();
    }

    private static IHostEnvironment CreateEnvironment()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        return environment;
    }

    private static string? ReadDomain(object parameters) =>
        ReadSqlParameter<string?>(parameters, "Domain");
}
