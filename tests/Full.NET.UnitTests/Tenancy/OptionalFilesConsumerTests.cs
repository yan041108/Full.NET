using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReconcileQuotaUsageBaseline;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Features.TenantBranding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>未装配 Files 时，基础服务仍可解析，媒体及文件用量操作必须在数据库访问前拒绝。</summary>
[TestClass]
public sealed class OptionalFilesConsumerTests
{
    [TestMethod]
    public void Consumers_resolve_without_optional_files_ports()
    {
        using var fixture = new Fixture();
        Assert.IsNotNull(fixture.Services.GetRequiredService<SelfServiceProfileMediaService>());
        Assert.IsNotNull(fixture.Services.GetRequiredService<TenantBrandingMediaService>());
        Assert.IsNotNull(fixture.Services.GetRequiredService<TenantQuotaUsageBaselineService>());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Storage_baseline_without_files_is_rejected_before_database_access(bool dryRun)
    {
        using var fixture = new Fixture();
        var result = await fixture.Services.GetRequiredService<TenantQuotaUsageBaselineService>()
            .ReconcileAsync(new ReconcileTenantQuotaUsageBaselineRequest(
                DryRun: dryRun, MetricCode: TenantQuotaMetricCodes.FilesStorageBytes));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.QuotaUsageBaselineUnsupportedMetric, result.Error!.Code);
        Assert.AreEqual(ErrorType.Validation, result.Error.Type);
        fixture.AssertNoDatabaseCalls();
    }

    [TestMethod]
    public async Task Seats_baseline_without_files_uses_identity_authoritative_count()
    {
        using var fixture = new Fixture();
        var tenantId = Guid.CreateVersion7();
        fixture.Queries.QueryAsync<TenantQuotaMetricBackfillCandidate>(
                Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TenantQuotaMetricBackfillCandidate>());
        fixture.Queries.QueryAsync<TenantQuotaMetricRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new TenantQuotaMetricRecord(
                Guid.CreateVersion7(), tenantId, TenantQuotaMetricCodes.IdentitySeats,
                TenantQuotaDefaults.PeriodKey, 100, 0, 0, 1) });
        fixture.Members.CountActiveMembersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await fixture.Services.GetRequiredService<TenantQuotaUsageBaselineService>()
            .ReconcileAsync(new ReconcileTenantQuotaUsageBaselineRequest(DryRun: true));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.CandidateCount);
        Assert.AreEqual(0, result.Value.AppliedCount);
        await fixture.Members.Received(1).CountActiveMembersAsync(tenantId, Arg.Any<CancellationToken>());
        Assert.AreEqual(0, fixture.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("upload-avatar")]
    [DataRow("upload-signature")]
    [DataRow("delete-avatar")]
    [DataRow("delete-signature")]
    [DataRow("read-avatar")]
    [DataRow("read-signature")]
    public async Task Profile_media_without_files_is_rejected_without_side_effects(string operation)
    {
        using var fixture = new Fixture();
        var service = fixture.Services.GetRequiredService<SelfServiceProfileMediaService>();
        var userId = Guid.CreateVersion7();
        using var content = new MemoryStream(new byte[] { 1 });
        var error = operation switch
        {
            "upload-avatar" => (await service.UploadAvatarAsync(userId, "host", "image.png", "image/png", content, 1)).Error,
            "upload-signature" => (await service.UploadSignatureAsync(userId, "host", "image.png", "image/png", content, 1)).Error,
            "delete-avatar" => (await service.DeleteAvatarAsync(userId, "host")).Error,
            "delete-signature" => (await service.DeleteSignatureAsync(userId, "host")).Error,
            "read-avatar" => (await service.OpenAvatarContentAsync(userId, "host")).Error,
            "read-signature" => (await service.OpenSignatureContentAsync(userId, "host")).Error,
            _ => throw new InvalidOperationException(operation),
        };

        Assert.IsNotNull(error);
        Assert.AreEqual(operation.StartsWith("read-", StringComparison.Ordinal)
            ? IdentityErrorCodes.SelfServiceProfileMediaNotFound
            : IdentityErrorCodes.SelfServiceProfileMediaInvalid, error.Code);
        fixture.AssertNoDatabaseCalls();
    }

    [TestMethod]
    [DataRow("upload-host")]
    [DataRow("upload-current")]
    [DataRow("delete-host")]
    [DataRow("delete-current")]
    [DataRow("read-host")]
    [DataRow("read-current")]
    public async Task Branding_media_without_files_is_rejected_without_side_effects(string operation)
    {
        using var fixture = new Fixture();
        var service = fixture.Services.GetRequiredService<TenantBrandingMediaService>();
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        using var content = new MemoryStream(new byte[] { 1 });
        var error = operation switch
        {
            "upload-host" => (await service.UploadLogoByTenantIdAsync(tenantId, userId, "image.png", "image/png", content, 1)).Error,
            "upload-current" => (await service.UploadLogoCurrentAsync(tenantId, userId, "image.png", "image/png", content, 1)).Error,
            "delete-host" => (await service.DeleteLogoByTenantIdAsync(tenantId)).Error,
            "delete-current" => (await service.DeleteLogoCurrentAsync(tenantId)).Error,
            "read-host" => (await service.OpenLogoContentAsync(tenantId)).Error,
            "read-current" => (await service.OpenCurrentLogoContentAsync()).Error,
            _ => throw new InvalidOperationException(operation),
        };

        Assert.IsNotNull(error);
        Assert.AreEqual(operation.StartsWith("read-", StringComparison.Ordinal)
            ? TenancyErrorCodes.BrandingLogoNotFound
            : TenancyErrorCodes.BrandingLogoInvalid, error.Code);
        fixture.AssertNoDatabaseCalls();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ICommandTransaction Transaction { get; } = Substitute.For<ICommandTransaction>();
        public ITenantActiveMemberCountPort Members { get; } = Substitute.For<ITenantActiveMemberCountPort>();
        public IServiceProvider Services => scope.ServiceProvider;

        public Fixture()
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddSingleton(Queries);
            registrations.AddSingleton(Commands);
            registrations.AddSingleton(Transaction);
            registrations.AddSingleton(Members);
            registrations.AddSingleton(Substitute.For<IClock>());
            registrations.AddSingleton(Substitute.For<IIdGenerator>());
            registrations.AddSingleton(Substitute.For<ICurrentTenant>());
            registrations.AddSingleton(Substitute.For<ICurrentTenantContextWriter>());
            registrations.AddSingleton(Substitute.For<IUserFieldProjectionResolver>());
            registrations.AddSingleton(Substitute.For<ICacheInvalidator>());
            registrations.AddSingleton(Substitute.For<ICachePolicyRegistry>());
            registrations.AddSingleton(Substitute.For<IHostEnvironment>());
            registrations.AddScoped<SelfServiceProfileService>();
            registrations.AddScoped<SelfServiceProfileMediaService>();
            registrations.AddScoped<TenantCacheInvalidator>();
            registrations.AddScoped<TenantBrandingService>();
            registrations.AddScoped<TenantBrandingMediaService>();
            registrations.AddScoped<TenantQuotaUsageBaselineService>();
            provider = registrations.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            scope = provider.CreateScope();
        }

        public void AssertNoDatabaseCalls()
        {
            Assert.AreEqual(0, Queries.ReceivedCalls().Count());
            Assert.AreEqual(0, Commands.ReceivedCalls().Count());
            Assert.AreEqual(0, Transaction.ReceivedCalls().Count());
        }

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
