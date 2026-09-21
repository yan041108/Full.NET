using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.TenantBranding;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>验证租户品牌字段校验与服务边界。</summary>
[TestClass]
public sealed class TenantBrandingServiceTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [TestMethod]
    public void Policy_rejects_executable_path_tokens()
    {
        Assert.IsFalse(TenantBrandingPolicy.TryValidateTextFields(
            "C:\\Windows\\System32\\cmd.exe",
            null,
            null,
            null,
            null));
    }

    [TestMethod]
    public void Policy_accepts_plain_contact_fields()
    {
        Assert.IsTrue(TenantBrandingPolicy.TryValidateTextFields(
            "演示租户",
            "400-000-0000",
            "ops@example.com",
            "上海市浦东新区",
            "Copyright 2026"));
    }

    [TestMethod]
    public async Task Update_rejects_invalid_email()
    {
        var fixture = new Fixture();
        fixture.SetupExistingBranding(version: 1);

        var result = await fixture.Service.UpdateByTenantIdAsync(
            TenantId,
            new UpdateTenantBrandingRequest(
                "演示",
                null,
                "not-an-email",
                null,
                null,
                1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.BrandingInvalid, result.Error!.Code);
    }

    [TestMethod]
    public async Task Update_persists_valid_fields()
    {
        var fixture = new Fixture();
        fixture.SetupUpdatedBranding();

        var result = await fixture.Service.UpdateByTenantIdAsync(
            TenantId,
            new UpdateTenantBrandingRequest(
                "演示租户",
                "400-000-0000",
                "ops@example.com",
                "上海市",
                "Copyright",
                2));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("演示租户", result.Value!.SystemTitle);
        await fixture.Command.Received(1).ExecuteAsync(
            TenantSql.UpdateTenantBranding,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetRuntime_returns_empty_branding_on_host_context()
    {
        var fixture = new Fixture();
        fixture.CurrentTenant.IsHost.Returns(true);
        fixture.CurrentTenant.IsAvailable.Returns(true);
        fixture.CurrentTenant.Id.Returns((Guid?)null);

        var result = await fixture.Service.GetRuntimeAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value!.HasLogo);
        await fixture.Query.DidNotReceive().QuerySingleOrDefaultAsync<TenantBrandingRecord>(
            Arg.Any<SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetCurrent_requires_tenant_context()
    {
        var fixture = new Fixture();
        fixture.CurrentTenant.IsAvailable.Returns(false);

        var result = await fixture.Service.GetCurrentAsync();

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.NotFound, result.Error!.Code);
    }

    private sealed class Fixture
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        public ICurrentTenant CurrentTenant { get; } = Substitute.For<ICurrentTenant>();
        public TenantBrandingService Service { get; }

        public Fixture()
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-06T00:00:00Z"));
            Command.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            CurrentTenant.IsAvailable.Returns(true);
            CurrentTenant.Id.Returns(TenantId);
            Query.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                    TenantSql.FindById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(new TenantResolutionRecord(
                    TenantId,
                    "demo",
                    "Demo",
                    "demo.localhost",
                    true,
                    3,
                    "zh-CN"));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddFusionCache().AsHybridCache();
            using var provider = services.BuildServiceProvider();
            var invalidator = TenantCacheInvalidatorTestFactory.Create(
                provider.GetRequiredService<IFusionCache>(),
                new TestHostEnvironment("Testing"));
            Service = new TenantBrandingService(
                Query,
                Command,
                new PassThroughTransaction(),
                clock,
                CurrentTenant,
                invalidator);
        }

        public void SetupExistingBranding(int version)
        {
            Query.QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                    TenantSql.FindTenantBrandingById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(new TenantBrandingRecord(
                    TenantId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    version));
        }

        public void SetupUpdatedBranding()
        {
            Query.QuerySingleOrDefaultAsync<TenantBrandingRecord>(
                    TenantSql.FindTenantBrandingById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(
                    new TenantBrandingRecord(
                        TenantId,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        2),
                    new TenantBrandingRecord(
                        TenantId,
                        "演示租户",
                        null,
                        "400-000-0000",
                        "ops@example.com",
                        "上海市",
                        "Copyright",
                        3));
        }
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
