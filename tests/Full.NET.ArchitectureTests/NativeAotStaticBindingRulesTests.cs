using System.Text.RegularExpressions;

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
        StringAssert.Contains(
            extensionsSource,
            "ConfigureHttpJsonOptions");
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

    [TestMethod]
    public void AccessSessionValidator_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var path = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity",
            "Security",
            "AccessSessionValidator.cs");
        var source = File.ReadAllText(path);

        Assert.IsFalse(
            source.Contains("new { SessionId", StringComparison.Ordinal),
            "JWT 验证发生在所有受保护 Endpoint 之前，会话查询参数必须使用 Native AOT 可静态执行的参数容器。");
        StringAssert.Contains(source, "IReadOnlyDictionary<string, object?>");
    }

    [TestMethod]
    public void ModuleValidationBehaviors_UseClosedGenericRegistrations()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleRoot = Path.Combine(root, "src", "Modules");
        var offenders = Directory.EnumerateFiles(
                moduleRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "AddFullNetFluentValidation();",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT 模块必须为有校验器的消息注册闭合 Behavior，避免 DI 在运行时创建未保留元数据的泛型数组："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void DomainAuditWriters_UseAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleRoot = Path.Combine(root, "src", "Modules");
        var offenders = Directory.EnumerateFiles(
                moduleRoot,
                "*DomainAuditWriter.cs",
                SearchOption.AllDirectories)
            .Where(path => !File.ReadAllText(path).Contains(
                "IReadOnlyDictionary<string, object?>",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT 域内审计写入必须使用静态 SQL 参数容器："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void HostFileServices_UseAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var path = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Files",
            "Features",
            "ManageHostFiles",
            "HostFileManagementService.cs");
        var source = File.ReadAllText(path);

        StringAssert.Contains(source, "IReadOnlyDictionary<string, object?>");
        Assert.IsFalse(
            source.Contains("HostFileSql.Insert,\n                                new", StringComparison.Ordinal)
            || source.Contains("HostFileSql.ClaimPublication,\n                                new", StringComparison.Ordinal)
            || source.Contains("HostFileSql.MarkReady,\n                                    new", StringComparison.Ordinal)
            || source.Contains("HostFileSql.SoftDelete,\n                new", StringComparison.Ordinal),
            "Native AOT 文件状态机 SQL 不得使用匿名参数。");

        var queryPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Files",
            "Features",
            "ManageHostFiles",
            "HostFileQueryService.cs");
        var querySource = File.ReadAllText(queryPath);

        StringAssert.Contains(querySource, "Dictionary<string, object?>");
        Assert.IsFalse(
            querySource.Contains("new {", StringComparison.Ordinal),
            "Native AOT 文件查询 SQL 不得使用匿名参数。");

        var filesSources = Directory
            .EnumerateFiles(
                Path.Combine(root, "src", "Modules", "Full.NET.Modules.Files"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.IsFalse(
            filesSources.Any(source => source.Contains("new {", StringComparison.Ordinal)),
            "Native AOT Files 模块不得向 SQL 执行器传递匿名参数。");
    }

    [TestMethod]
    public void MessagingAuditSchema_AllowsRequestedReplayOutcome()
    {
        var root = ArchitectureRepositoryRoot.Find();
        foreach (var provider in new[] { "SqlServer", "MySql" })
        {
            var path = Path.Combine(
                root,
                "src",
                "BuildingBlocks",
                "Full.NET.Migrations.DbUp",
                "Migrations",
                provider,
                "100_MessagingDomainAuditRequestedOutcome.sql");
            var source = File.ReadAllText(path);

            StringAssert.Contains(
                source,
                "Outcome IN ('requested', 'success', 'failure')");
        }
    }

    [TestMethod]
    public void ApiNativeAot_PreservesConfluentKafkaLinuxNativeMethods()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var hostDirectory = Path.Combine(root, "src", "Hosts", "Full.NET.Host.Api");
        var projectSource = File.ReadAllText(Path.Combine(hostDirectory, "Full.NET.Host.Api.csproj"));
        var rootsSource = File.ReadAllText(Path.Combine(hostDirectory, "NativeAotRoots.xml"));

        StringAssert.Contains(projectSource, "<RdXmlFile Include=\"NativeAotRoots.xml\" />");
        foreach (var nativeMethodsType in new[]
                 {
                     "Confluent.Kafka.Impl.NativeMethods.NativeMethods",
                     "Confluent.Kafka.Impl.NativeMethods.NativeMethods_Centos8",
                     "Confluent.Kafka.Impl.NativeMethods.NativeMethods_Alpine",
                 })
        {
            StringAssert.Contains(rootsSource, $"<Type Name=\"{nativeMethodsType}\" Dynamic=\"Required All\" />");
        }
    }

    [TestMethod]
    public void FilesModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Files");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "FilesModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "FilesDapperAotMaterializerContributor.cs"));

        StringAssert.Contains(moduleSource, "FilesDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "HostFileListRecord",
                     "HostFileDetailRecord",
                     "DeletedHostFileBlobRecord",
                     "PendingHostFileRecord",
                     "HostFileReferenceClaimRecord",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }
    }

    [TestMethod]
    public void EventStreamOwnershipGate_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper",
            "Outbox",
            "DapperEventStreamOwnershipGate.cs"));

        StringAssert.Contains(source, "IReadOnlyDictionary<string, object?>");
        Assert.IsFalse(
            source.Contains("new {", StringComparison.Ordinal),
            "Native AOT 事件流所有权门禁不得使用匿名参数。");
    }

    [TestMethod]
    public void EventStreamOwnershipStore_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Messaging",
            "Persistence",
            "EventStreamOwnershipStore.cs"));

        Assert.IsFalse(
            source.Contains("new {", StringComparison.Ordinal),
            "Native AOT 事件流所有权存储不得使用匿名 SQL 参数。");
    }

    [TestMethod]
    public void DapperNativeAotScalarReader_SupportsNullableScalars()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper",
            "DapperAotSqlExecution.cs"));

        StringAssert.Contains(source, "Nullable.GetUnderlyingType(type) ?? type");
        StringAssert.Contains(source, "Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)");
    }

    [TestMethod]
    public void NotificationsModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Notifications");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Notifications 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void NotificationsModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Notifications");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "NotificationsModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "NotificationsDapperAotMaterializerContributor.cs"));
        var announcementSqlSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "AnnouncementSql.cs"));
        var inboxSqlSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "InboxMessageSql.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "NotificationsDapperAotMaterializerContributor");
        foreach (var recordType in new[] { "AnnouncementRecord", "InboxMessageRecord" })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }

        const string announcementProjection =
            "Id, TenantId, Title, Content, Status, PublishedAtUtc, "
            + "CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version";
        foreach (var statement in new[]
                 {
                     "ListHostSqlServer",
                     "ListHostMySql",
                     "FindHostById",
                 })
        {
            Assert.AreEqual(
                announcementProjection,
                ExtractSelectProjection(announcementSqlSource, statement),
                $"Announcement SQL 投影顺序必须一致：{statement}");
        }

        const string inboxProjection =
            "Id, TenantId, RecipientUserId, Title, Content, Status, "
            + "ReadAtUtc, CreatedAtUtc, CreatedByUserId";
        foreach (var statement in new[]
                 {
                     "ListForRecipientSqlServer",
                     "ListForRecipientMySql",
                     "FindForRecipientById",
                 })
        {
            Assert.AreEqual(
                inboxProjection,
                ExtractSelectProjection(inboxSqlSource, statement),
                $"Inbox SQL 投影顺序必须一致：{statement}");
        }
    }

    [TestMethod]
    public void DapperInbox_RegistersAotMaterializersAndUsesSafeParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var dapperDirectory = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper");
        var inboxSource = File.ReadAllText(Path.Combine(
            dapperDirectory,
            "Inbox",
            "DapperIntegrationEventInbox.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(
            dapperDirectory,
            "DapperAotInfrastructureRegistration.cs"));

        Assert.IsFalse(
            inboxSource.Contains("new {", StringComparison.Ordinal),
            "Native AOT Inbox 不得使用匿名 SQL 参数。");
        StringAssert.Contains(registrationSource, "Register<InboxClaimRow>");
        StringAssert.Contains(registrationSource, "Register<InboxBatchPrecheckRow>");
    }

    private static bool ContainsAnonymousSqlParameterObject(string source) =>
        source.Contains("new {", StringComparison.Ordinal)
        || Regex.IsMatch(source, @"new\s*\{", RegexOptions.CultureInvariant);

    private static string ExtractSelectProjection(string source, string statementField)
    {
        var fieldIndex = source.IndexOf(
            $"SqlStatement {statementField}",
            StringComparison.Ordinal);
        Assert.IsTrue(fieldIndex >= 0, $"未找到 SQL 语句字段：{statementField}");

        var selectIndex = source.IndexOf("SELECT", fieldIndex, StringComparison.Ordinal);
        var fromIndex = source.IndexOf("FROM", selectIndex, StringComparison.Ordinal);
        Assert.IsTrue(selectIndex >= 0 && fromIndex > selectIndex, $"未找到 SELECT 投影：{statementField}");

        return Regex.Replace(
            source[(selectIndex + "SELECT".Length)..fromIndex],
            @"\s+",
            " ").Trim();
    }
}
