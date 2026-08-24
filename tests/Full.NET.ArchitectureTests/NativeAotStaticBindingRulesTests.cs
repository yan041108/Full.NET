namespace Full.NET.ArchitectureTests;

/// <summary>
/// 防止 Hosting/Caching/Messaging 在 API 闭包中回退到不可静态分析的配置绑定与 JSON 序列化路径。
/// </summary>
[TestClass]
public sealed class NativeAotStaticBindingRulesTests
{
    private static readonly string[] WatchedRelativePaths =
    [
        "src/BuildingBlocks/Full.NET.Hosting",
        "src/BuildingBlocks/Full.NET.Caching.Fusion",
        "src/BuildingBlocks/Full.NET.Messaging.Abstractions",
        "src/BuildingBlocks/Full.NET.Realtime.SignalR",
    ];

    private static readonly string[] ForbiddenBindingPatterns =
    [
        ".Bind(configuration.GetSection(",
        ".Bind(builder.Configuration.GetSection(",
        ").Bind(options)",
        ").Bind(cacheOptions)",
        ").Bind(loggingOptions)",
        "ConfigurationBinder.Bind(",
    ];

    [TestMethod]
    public void HostingCachingMessaging_AvoidDynamicConfigurationBinding()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var offenders = WatchedRelativePaths
            .SelectMany(relativePath => Directory.EnumerateFiles(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                "*.cs",
                SearchOption.AllDirectories))
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path),
                Content = File.ReadAllText(path),
            })
            .Where(file => ForbiddenBindingPatterns.Any(pattern =>
                file.Content.Contains(pattern, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Hosting/Caching/Messaging/Realtime 必须使用 BindConfiguration 或源生成 Get<T>，"
                + $"禁止动态 Bind：{string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void CdcDeliveryPosition_UsesSourceGeneratedJsonContext()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var path = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Messaging.Abstractions",
            "CdcDeliveryPosition.cs");
        var source = File.ReadAllText(path);

        StringAssert.Contains(source, "MessagingJsonSerializerContext.Default");
        Assert.IsFalse(
            source.Contains("JsonSerializerOptions", StringComparison.Ordinal),
            "CDC 位点 JSON 不得保留运行期 JsonSerializerOptions。");
        Assert.IsFalse(
            source.Contains("JsonSerializer.Serialize<T>", StringComparison.Ordinal),
            "CDC 位点序列化必须显式传入 JsonTypeInfo。");
    }

    [TestMethod]
    public void FullNetNotificationHub_DoesNotUseTypedClientProxy()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var path = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Realtime.SignalR",
            "FullNetNotificationHub.cs");
        var source = File.ReadAllText(path);

        Assert.IsFalse(
            source.Contains("Hub<IFullNetNotificationClient>", StringComparison.Ordinal),
            "Native AOT 路径禁止 Hub<T> 动态代理。");
        StringAssert.Contains(source, ": Hub");
    }

    [TestMethod]
    public void RealtimeSignalR_UsesSourceGeneratedJsonContext()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var extensionsPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Realtime.SignalR",
            "ServiceCollectionExtensions.cs");
        var extensionsSource = File.ReadAllText(extensionsPath);

        StringAssert.Contains(
            extensionsSource,
            "RealtimeJsonSerializerContext.Default");
        Assert.IsFalse(
            extensionsSource.Contains("AddMessagePackProtocol", StringComparison.Ordinal),
            "SignalR 已统一 JSON 协议，不得注册 MessagePack Hub 协议。");
    }

    [TestMethod]
    public void FusionCacheL2_UsesSourceGeneratedJsonContext()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var serializerPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Caching.Fusion",
            "Serialization",
            "FullNetFusionCacheJsonSerializer.cs");
        var contextPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Caching.Fusion",
            "Serialization",
            "FusionCacheJsonSerializerContext.cs");
        var extensionsPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Caching.Fusion",
            "ServiceCollectionExtensions.cs");

        var serializerSource = File.ReadAllText(serializerPath);
        var contextSource = File.ReadAllText(contextPath);
        var extensionsSource = File.ReadAllText(extensionsPath);

        StringAssert.Contains(serializerSource, "FusionCacheJsonSerializerContext.Default");
        StringAssert.Contains(contextSource, "TenantResolutionCacheEntry");
        StringAssert.Contains(contextSource, "GridPreferenceResponse");
        StringAssert.Contains(contextSource, "DiagnosticPolicyDocument");
        StringAssert.Contains(
            extensionsSource,
            "WithSerializer(new FullNetFusionCacheJsonSerializer())");
        Assert.IsFalse(
            extensionsSource.Contains(
                "#if FULLNET_AOT_COMPILE",
                StringComparison.Ordinal)
                && extensionsSource.Contains(
                    ".AsHybridCache();",
                    StringComparison.Ordinal)
                && !extensionsSource.Contains(
                    "TryWithRegisteredDistributedCache()",
                    StringComparison.Ordinal),
            "Native AOT 缓存路径必须保留 Redis L2 与 Backplane 注册。");
    }
}
