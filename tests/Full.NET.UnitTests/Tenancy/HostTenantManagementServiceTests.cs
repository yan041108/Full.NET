using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Auditing;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class HostTenantManagementServiceTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Enable_activates_inactive_tenant_and_writes_audit()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(fixture.DefaultTenant with { IsActive = false });

        var result = await fixture.Service.EnableAsync(TenantId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            TenantSql.EnableHostTenant,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.AuditWriter.Received(1).WriteAsync(
            Arg.Is<TenancyDomainAuditWrite>(write =>
                write != null
                && write.ActionKey == TenancyDomainAuditActionKeys.HostTenantEnable),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Enable_is_idempotent_for_active_tenant()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.EnableAsync(TenantId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            TenantSql.EnableHostTenant,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Disable_rejects_last_active_tenant()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<long>(
                TenantSql.CountActiveTenants,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await fixture.Service.DisableAsync(TenantId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.LastActiveTenant, result.Error!.Code);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            TenantSql.DisableHostTenant,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Query = Substitute.For<IQueryExecutor>();
            DefaultTenant = new TenantResolutionRecord(
                TenantId,
                "demo",
                "Demo Tenant",
                "demo.localhost",
                true,
                1,
                "zh-CN");
            Query.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(DefaultTenant);
            Query.QuerySingleOrDefaultAsync<HostTenantRecord>(
                    TenantSql.FindHostTenantById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(new HostTenantRecord(
                    TenantId,
                    DefaultTenant.Identifier,
                    DefaultTenant.Name,
                    DefaultTenant.Domain,
                    DefaultTenant.IsActive,
                    DefaultTenant.Version,
                    DefaultTenant.DefaultLocale,
                    null,
                    null,
                    null,
                    TenantLifecycleStatuses.Active,
                    null,
                    TenantProvisioningStatuses.Completed,
                    null));
            Query.QuerySingleOrDefaultAsync<long>(
                    TenantSql.CountActiveTenants,
                    cancellationToken: Arg.Any<CancellationToken>())
                .Returns(2);
            Command = Substitute.For<ICommandExecutor>();
            Command.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            AuditWriter = Substitute.For<ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-06T00:00:00Z"));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFusionCache().AsHybridCache();
            using var provider = services.BuildServiceProvider();
            var invalidator = TenantCacheInvalidatorTestFactory.Create(
                provider.GetRequiredService<IFusionCache>(),
                new TestHostEnvironment("Testing"));
            var packageBinder = new TenantHostPackageBinder(Query, Command, clock);
            Service = new HostTenantManagementService(
                Query,
                Command,
                new PassThroughTransaction(),
                new HostTenantQueryService(
                    Query,
                    Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
                    {
                        Provider = DatabaseProvider.SqlServer,
                    })),
                packageBinder,
                clock,
                invalidator,
                AuditWriter);
        }

        public TenantResolutionRecord DefaultTenant { get; }

        public ICommandExecutor Command { get; }

        public IQueryExecutor Query { get; }

        public ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> AuditWriter { get; }

        public HostTenantManagementService Service { get; }
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
