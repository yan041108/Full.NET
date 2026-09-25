using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Api;

/// <summary>为 API/Worker 跨进程恢复集成测试构建只运行目标 HostedService 的 Worker 宿主。</summary>
internal static class IntegrationWorkerHostFactory
{
    /// <summary>创建共享测试数据库与 Redis、并仅保留指定后台服务的 Worker 宿主。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">当前测试专用数据库连接串。</param>
    /// <param name="workerSettings">目标 Worker 与数据库的覆盖配置。</param>
    /// <param name="applicationName">宿主诊断名称。</param>
    /// <param name="hostedServiceName">唯一保留的 HostedService 实现类型名。</param>
    /// <returns>尚未启动的 Worker 宿主。</returns>
    public static async Task<IHost> BuildAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?> workerSettings,
        string applicationName,
        string hostedServiceName)
    {
        var redisConnectionString = await SharedDatabaseFixture.GetRedisConnectionStringAsync()
            .ConfigureAwait(false);
        var settings = new Dictionary<string, string?>(workerSettings)
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] = "Binary16",
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            ["Cache:RedisConnectionString"] = redisConnectionString,
            ["Realtime:RedisBackplaneConnectionString"] = redisConnectionString,
            ["Realtime:AllowSharedRedisInDevelopment"] = "true",
            ["ConnectionStrings:redis"] = redisConnectionString,
            ["Files:Local:RootPath"] = Path.Combine(
                Path.GetTempPath(),
                "fullnet-files-integration",
                $"worker-{Guid.NewGuid():N}"),
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var builder = global::Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ApplicationName = applicationName,
                EnvironmentName = "Testing",
            });
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddScoped<CurrentTenantAccessor>();
        builder.Services.AddScoped<ICurrentTenant>(services =>
            services.GetRequiredService<CurrentTenantAccessor>());
        builder.Services.AddScoped<ICurrentTenantContextWriter>(services =>
            services.GetRequiredService<CurrentTenantAccessor>());
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        builder.Services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        builder.Services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        builder.Services.AddSingleton<
            IIdentityOrganizationUnitDirectory,
            EmptyIdentityOrganizationUnitDirectory>();
        builder.Services.AddFullNetDapper(configuration, "Testing");
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetCaching(configuration, "Testing");
        builder.Services.AddFullNetRealtimePublisher(configuration, "Testing");
        builder.Services.AddFullNetApplicationModules(configuration, FullNetHostProfile.Worker);
        foreach (var descriptor in builder.Services
                     .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                         && !string.Equals(
                             descriptor.ImplementationType?.Name,
                             hostedServiceName,
                             StringComparison.Ordinal))
                     .ToArray())
        {
            builder.Services.Remove(descriptor);
        }

        return builder.Build();
    }

    private sealed class EmptyTenantOrganizationUnitDirectory : ITenantOrganizationUnitDirectory
    {
        public Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class EmptyIdentityOrganizationUnitDirectory : IIdentityOrganizationUnitDirectory
    {
        public Task<IdentityOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("非 HTTP Worker 集成夹具不映射 API 结果。");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("非 HTTP Worker 集成夹具不映射 API 异常。");
    }
}
