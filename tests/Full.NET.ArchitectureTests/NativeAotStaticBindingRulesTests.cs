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
    public void HostUserDirectory_FindActiveHostUser_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity",
            "HostUsers",
            "HostUserDirectory.cs"));

        var findActiveIndex = source.IndexOf(
            "FindActiveHostUserAsync",
            StringComparison.Ordinal);
        Assert.IsTrue(findActiveIndex >= 0, "未找到 FindActiveHostUserAsync。");
        var methodOpen = source.IndexOf('{', findActiveIndex);
        Assert.IsTrue(methodOpen > findActiveIndex, "未找到 FindActiveHostUserAsync 方法体。");
        var methodClose = source.LastIndexOf('}');
        var findActiveBody = source[methodOpen..methodClose];
        Assert.IsFalse(
            findActiveBody.Contains("new {", StringComparison.Ordinal),
            "Native AOT Host 用户活动校验不得使用匿名 SQL 参数。");
        StringAssert.Contains(findActiveBody, "Dictionary<string, object?>");
        StringAssert.Contains(findActiveBody, "[\"UserId\"]");
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
    public void MessagingModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Messaging");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "MessagingModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "MessagingDapperAotMaterializerContributor.cs"));
        var ownershipSqlSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "EventStreamOwnershipSql.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "MessagingDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "EventStreamOwnershipPersistenceRow",
                     "RollbackPreparationRecord",
                     "OutboxStreamCutoffRecord",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }

        const string ownershipProjection =
            "MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner, "
            + "CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId, "
            + "Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc, "
            + "RollbackState, RollbackGeneration, RollbackPreparedAtUtc, "
            + "CreatedAtUtc, UpdatedAtUtc";
        foreach (var statement in new[] { "FindByStream", "ListAll" })
        {
            Assert.AreEqual(
                ownershipProjection,
                ExtractSelectProjection(ownershipSqlSource, statement),
                $"Stream ownership SQL 投影顺序必须一致：{statement}");
        }

        const string rollbackProjection =
            "RollbackState, RollbackGeneration, RollbackPreparedAtUtc";
        Assert.AreEqual(
            rollbackProjection,
            ExtractSelectProjection(ownershipSqlSource, "FindRollbackPreparation"),
            "Rollback preparation SQL 投影顺序必须一致。");

        const string cutoffProjection = "Id AS CutoffEventId, OccurredAtUtc AS CutoffOccurredAtUtc";
        Assert.AreEqual(
            $"TOP 1 {cutoffProjection}",
            ExtractSelectProjection(
                ownershipSqlSource,
                "FindLastAppendOnlyOutboxEventByStreamSqlServer"),
            "Append-only cutoff SQL Server 投影顺序必须一致。");
        Assert.AreEqual(
            cutoffProjection,
            ExtractSelectProjection(
                ownershipSqlSource,
                "FindLastAppendOnlyOutboxEventByStreamMySql"),
            "Append-only cutoff MySQL 投影顺序必须一致。");
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
    public void SettingsModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Settings");
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
            "Native AOT Settings 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void JobsModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Jobs");
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
            "Native AOT Jobs 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void SerialNumbersModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.SerialNumbers");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT SerialNumbers 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void SerialNumbersModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.SerialNumbers");
        var moduleSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "SerialNumbersModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "SerialNumbersDapperAotMaterializerContributor.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(
            moduleSource,
            "SerialNumbersDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "SerialNumberRuleRecord",
                     "AllocatedCounterValue",
                     "SerialNumberAllocationRecord",
                 })
        {
            StringAssert.Contains(
                contributorSource,
                $"registrar.Register<{recordType}>");
        }
    }

    [TestMethod]
    public void DocumentModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Document");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Document 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void DocumentModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Document");
        var moduleSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "DocumentModule.cs"));
        var contributorPath = Path.Combine(
            moduleDirectory,
            "Persistence",
            "DocumentDapperAotMaterializerContributor.cs");

        Assert.IsTrue(File.Exists(contributorPath), "Document AOT 物化器 contributor 尚未建立。");
        var contributorSource = File.ReadAllText(contributorPath);
        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "DocumentDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "DocumentCategoryRecord",
                     "DocumentTagRecord",
                     "DocumentNameConflictRecord",
                     "DocumentItemRecord",
                     "DocumentItemDetailRecord",
                     "DocumentVersionRecord",
                     "DocumentPermissionRecord",
                     "DocumentShareRecord",
                     "DocumentStatisticsSummaryRecord",
                     "DocumentStatisticsByTypeRecord",
                     "DocumentStatisticsByCategoryRecord",
                     "DocumentStatisticsShareCountRecord",
                 })
        {
            StringAssert.Contains(
                contributorSource,
                $"registrar.Register<{recordType}>");
        }

        StringAssert.Contains(
            contributorSource,
            "private static int RequiredOrdinal(DbDataReader reader, string name) => reader.GetOrdinal(name);");
        StringAssert.Contains(
            contributorSource,
            "DocumentNo = ReadOptionalString(reader, \"DocumentNo\")");
        StringAssert.Contains(
            contributorSource,
            "ChangeDescription = ReadOptionalNullableString(reader, \"ChangeDescription\")");
        Assert.IsFalse(
            contributorSource.Contains(
                "return ordinal < 0 ? Guid.Empty",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void AuditingModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Auditing");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Auditing 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void AuditingModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Auditing");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "AuditingModule.cs"));
        var contributorPath = Path.Combine(
            moduleDirectory,
            "Persistence",
            "AuditingDapperAotMaterializerContributor.cs");

        Assert.IsTrue(File.Exists(contributorPath), "Auditing AOT 物化器 contributor 尚未建立。");
        var contributorSource = File.ReadAllText(contributorPath);
        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "AuditingDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "HostAccessLogQueryService.AccessLogRecord",
                     "HostOperationLogQueryService.OperationLogRecord",
                     "HostExceptionLogQueryService.ExceptionLogRecord",
                     "OutboundCallLogRecord",
                     "HostDashboardAccessMetricsRecord",
                     "HostDashboardActivityRecord",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }
    }

    [TestMethod]
    public void CodeGenerationModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.CodeGeneration");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT CodeGeneration 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void CodeGenerationModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.CodeGeneration");
        var contributorPath = Path.Combine(
            moduleDirectory,
            "Persistence",
            "CodeGenerationDapperAotMaterializerContributor.cs");
        var contributorSource = File.ReadAllText(contributorPath);

        foreach (var recordType in new[]
                 {
                     "CodeGenerationCatalogTableRow",
                     "CodeGenerationCatalogColumnRow",
                     "CodeGenerationTemplateRecord",
                     "CodeGenerationRunRecord",
                     "CodeGenerationCheckpointCleanupCandidate",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }
    }

    [TestMethod]
    public void MessagingModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Messaging");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Messaging 模块不得向 SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void MessagingOperations_RegistersNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var contributorPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Messaging",
            "Persistence",
            "MessagingDapperAotMaterializerContributor.cs");
        var contributorSource = File.ReadAllText(contributorPath);

        StringAssert.Contains(contributorSource, "registrar.Register<DeadLetterRecord>");
        StringAssert.Contains(contributorSource, "registrar.Register<OutboxEnvelopeRecord>");
    }

    [TestMethod]
    public void TenancyModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Tenancy");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Tenancy 模块不得向 Host.Api SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void TenancyModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var contributorPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Tenancy",
            "Persistence",
            "TenancyDapperAotMaterializerContributor.cs");
        var contributorSource = File.ReadAllText(contributorPath);

        StringAssert.Contains(contributorSource, "registrar.Register<HostTenantRecord>");
        StringAssert.Contains(contributorSource, "registrar.Register<TenantResolutionRecord>");
        StringAssert.Contains(contributorSource, "registrar.Register<TenantPackageRecord>");
        StringAssert.Contains(contributorSource, "registrar.Register<TenantPackageIdentityRecord>");
        StringAssert.Contains(contributorSource, "registrar.Register<LocalTenantSeedSummary>");
    }

    [TestMethod]
    public void OrganizationModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Organization");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Organization 模块不得向 Host.Api SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void OrganizationModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Organization");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "OrganizationModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "OrganizationDapperAotMaterializerContributor.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "OrganizationDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "OrganizationUnitRecord",
                     "OrganizationUnitListRow",
                     "OrganizationUnitParentLink",
                     "OrganizationUnitSnapshotRow",
                     "OrganizationUserUnitRecord",
                     "OrganizationUserUnitListRow",
                     "OrganizationUserPositionRecord",
                     "OrganizationUserPositionListRow",
                     "OrganizationPositionRecord",
                     "OrganizationPositionListRow",
                     "OrganizationPositionLevelRecord",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }

        foreach (var parameterType in new[]
                 {
                     "InsertOrganizationUnit",
                     "InsertOrganizationPosition",
                     "InsertOrganizationPositionLevel",
                     "InsertOrganizationUserUnit",
                     "InsertOrganizationUserPosition",
                 })
        {
            StringAssert.Contains(
                contributorSource,
                $"DapperAotParameterRegistry.Register<{parameterType}>");
        }
    }

    [TestMethod]
    public void IdentityModule_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Identity 模块不得向 Host.Api SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void IdentityModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "IdentityModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "IdentityDapperAotMaterializerContributor.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "IdentityDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "IdentityUserRecord",
                     "IdentityAuthorizationRow",
                     "IdentityProfileRecord",
                     "RefreshSessionRecord",
                     "HostUserDirectoryRecord",
                     "HostUserListRow",
                     "HostUserPreferredLocaleRow",
                     "HostUserFailedLoginCountRow",
                     "HostUserLockoutEndUtcRow",
                     "HostUserProfileRecord",
                     "HostRoleListRow",
                     "IdentityRoleRecord",
                     "IdentityRolePermission",
                     "IdentityUserRoleDataScopeRow",
                     "IdentityNavigationRecord",
                     "HostMenuListRow",
                     "HostNavigationCatalogSyncService.HostMenuSyncRow",
                     "HostNavigationCatalogSyncService.HostMenuRouteNameRow",
                     "OnlineSessionListRow",
                     "OnlineSessionRevokeRow",
                     "ApiKeyListRow",
                     "ApiKeyAuthenticationRow",
                     "IdentityUserTotpRecord",
                     "OrganizationUnitProjectionRecord",
                     "UserFieldProjectionGrantRow",
                     "SuperAdministratorResponse",
                     "SuperAdministratorAuditResponse",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }
    }

    [TestMethod]
    public void SettingsModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Settings");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "SettingsModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "SettingsDapperAotMaterializerContributor.cs"));
        var dictTypeSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "DictTypeSql.cs"));
        var dictItemSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "DictItemSql.cs"));
        var tenantDictTypeSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "TenantDictTypeSql.cs"));
        var tenantDictItemSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "TenantDictItemSql.cs"));
        var configSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "ConfigEntrySql.cs"));
        var gridSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "GridPreferenceSql.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "SettingsDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "HostDictTypes.DictTypeRecord",
                     "TenantDictTypes.DictTypeRecord",
                     "HostDictTypes.DictTypeIdentityRecord",
                     "TenantDictTypes.DictTypeIdentityRecord",
                     "HostDictItems.DictItemRecord",
                     "TenantDictItems.DictItemRecord",
                     "HostDictItems.DictItemIdentityRecord",
                     "TenantDictItems.DictItemIdentityRecord",
                     "ConfigEntryRecord",
                     "ConfigEntryIdentityRecord",
                     "ConfigEntrySecretRecord",
                     "GridPreferenceRecord",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }

        const string dictTypeProjection =
            "Id, Code, Name, Description, DisplayOrder, IsActive, "
            + "CreatedAtUtc, UpdatedAtUtc, Version";
        foreach (var statement in new[]
                 {
                     "ListHostDictTypesSqlServer",
                     "ListHostDictTypesMySql",
                     "FindById",
                     "ListAllHostDictTypes",
                 })
        {
            Assert.AreEqual(
                dictTypeProjection,
                ExtractSelectProjection(dictTypeSql, statement),
                $"Host 字典类型 SQL 投影顺序必须一致：{statement}");
        }

        const string dictItemProjection =
            "Id, DictTypeId, Label, Value, Color, DisplayOrder, IsActive, "
            + "CreatedAtUtc, UpdatedAtUtc, Version";
        foreach (var statement in new[]
                 {
                     "ListByTypeIdSqlServer",
                     "ListByTypeIdMySql",
                     "FindById",
                 })
        {
            Assert.AreEqual(
                dictItemProjection,
                ExtractSelectProjection(dictItemSql, statement),
                $"Host 字典项 SQL 投影顺序必须一致：{statement}");
        }

        foreach (var statement in new[]
                 {
                     "ListTenantDictTypesSqlServer",
                     "ListTenantDictTypesMySql",
                     "FindById",
                 })
        {
            Assert.AreEqual(
                dictTypeProjection,
                ExtractSelectProjection(tenantDictTypeSql, statement),
                $"租户字典类型 SQL 投影顺序必须一致：{statement}");
        }

        const string tenantDictItemProjection =
            "item.Id, item.DictTypeId, item.Label, item.Value, item.Color, "
            + "item.DisplayOrder, item.IsActive, item.CreatedAtUtc, item.UpdatedAtUtc, item.Version";
        foreach (var statement in new[]
                 {
                     "ListByTypeIdSqlServer",
                     "ListByTypeIdMySql",
                     "FindById",
                 })
        {
            Assert.AreEqual(
                tenantDictItemProjection,
                ExtractSelectProjection(tenantDictItemSql, statement),
                $"租户字典项 SQL 投影顺序必须一致：{statement}");
        }

        const string configProjection =
            "Id, ConfigKey, DisplayName, Description, GroupName, ValueKind, Value, "
            + "DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc, Version";
        foreach (var statement in new[]
                 {
                     "ListHostConfigEntriesSqlServer",
                     "ListHostConfigEntriesMySql",
                     "FindById",
                     "FindByKey",
                     "ListAllHostConfigEntries",
                 })
        {
            Assert.AreEqual(
                configProjection,
                ExtractSelectProjection(configSql, statement),
                $"配置项 SQL 投影顺序必须一致：{statement}");
        }

        Assert.AreEqual(
            "Id, UserId, GridKey, SchemaVersion, ColumnsJson, CreatedAtUtc, UpdatedAtUtc, Version",
            ExtractSelectProjection(gridSql, "FindByUserAndGrid"),
            "网格偏好 SQL 投影顺序必须一致。");
        Assert.AreEqual(
            "ValueKind, Value, IsActive",
            ExtractSelectProjection(configSql, "FindSecretByKey"),
            "Secret 配置项最小投影顺序必须一致。");
    }

    [TestMethod]
    public void JobsModule_RegistersAllNativeAotRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Jobs");
        var moduleSource = File.ReadAllText(Path.Combine(moduleDirectory, "JobsModule.cs"));
        var contributorSource = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Persistence",
            "JobsDapperAotMaterializerContributor.cs"));
        var jobSql = File.ReadAllText(Path.Combine(moduleDirectory, "Persistence", "JobSql.cs"));

        StringAssert.Contains(moduleSource, "#if FULLNET_AOT_COMPILE");
        StringAssert.Contains(moduleSource, "JobsDapperAotMaterializerContributor");
        foreach (var recordType in new[]
                 {
                     "JobDefinitionRecord",
                     "JobExecutionRecord",
                     "JobDefinitionOptionRecord",
                     "JobScheduleRecord",
                     "JobScheduleDetailRecord",
                     "JobWorkerInstanceRecord",
                     "JobsBacklogSqlServerRow",
                     "JobsBacklogMySqlRow",
                 })
        {
            StringAssert.Contains(contributorSource, $"registrar.Register<{recordType}>");
        }

        const string definitionProjection =
            "Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, GroupName, IsEnabled, "
            + "AllowConcurrentExecutions, CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version";
        foreach (var statement in new[]
                 {
                     "ListDefinitionsSqlServer",
                     "ListDefinitionsMySql",
                     "FindDefinitionById",
                     "FindDefinitionsByIds",
                     "FindDefinitionByJobKey",
                 })
        {
            Assert.AreEqual(
                definitionProjection,
                ExtractSelectProjection(jobSql, statement),
                $"任务定义 SQL 投影顺序必须一致：{statement}");
        }

        const string executionProjection =
            "e.Id, e.TenantId, e.JobDefinitionId, e.JobScheduleId, "
            + "e.Status, e.TriggerKind, e.ScheduledForUtc, "
            + "e.ErrorMessage, e.StartedAtUtc, e.FinishedAtUtc, "
            + "e.LeaseId, e.LeaseExpiresAtUtc, e.NextAttemptAtUtc, "
            + "e.AttemptCount, e.CreatedAtUtc, d.JobKey";
        foreach (var statement in new[]
                 {
                     "ListExecutionsSqlServer",
                     "ListExecutionsMySql",
                     "FindExecutionById",
                     "SelectExecutionsByLeaseMySql",
                 })
        {
            Assert.AreEqual(
                executionProjection,
                ExtractSelectProjection(jobSql, statement),
                $"任务执行 SQL 投影顺序必须一致：{statement}");
        }

        StringAssert.Contains(
            jobSql,
            "inserted.JobScheduleId, inserted.Status, inserted.TriggerKind");
        StringAssert.Contains(jobSql, "inserted.ScheduledForUtc");
        StringAssert.Contains(jobSql, "CAST(NULL AS varchar(64)) AS JobKey");

        const string scheduleProjection =
            "s.Id, s.TenantId, s.JobDefinitionId, s.TriggerKind, s.CronExpression, "
            + "s.TimeZoneId, s.OneTimeAtUtc, s.MisfirePolicy, s.IsEnabled, "
            + "s.NextExecutionAtUtc, s.LastExecutionAtUtc, s.CompletedAtUtc, "
            + "s.NumberOfRuns, s.NumberOfErrors, s.StartTime, s.EndTime, s.Args, "
            + "s.CreatedAtUtc, s.CreatedByUserId, s.UpdatedAtUtc, s.UpdatedByUserId, "
            + "s.Version, d.AllowConcurrentExecutions";
        Assert.AreEqual(
            scheduleProjection,
            ExtractSelectProjection(jobSql, "FindScheduleById"),
            "按 Id 查找计划必须与到期领取共用 JobScheduleRecord 序数，含 AllowConcurrentExecutions。");
        Assert.AreEqual(
            scheduleProjection,
            ExtractSelectProjection(jobSql, "SelectDueSchedulesMySql"),
            "MySQL 到期计划投影必须与 FindScheduleById 一致。");
        Assert.AreEqual(
            "InstanceId, HostProfile, StartedAtUtc, LastHeartbeatAtUtc, WorkerVersion",
            ExtractSelectProjection(jobSql, "ListWorkerInstances"),
            "Worker 心跳列表投影顺序必须一致。");
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

    [TestMethod]
    public void DapperOutbox_RegistersFixedCommandPlansAndAppendParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var dapperDirectory = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper");
        var registrationSource = File.ReadAllText(Path.Combine(
            dapperDirectory,
            "DapperAotInfrastructureRegistration.cs"));
        var executionSource = File.ReadAllText(Path.Combine(
            dapperDirectory,
            "DapperAotSqlExecution.cs"));

        StringAssert.Contains(
            registrationSource,
            "Register<AppendOnlyOutboxMessage>");
        StringAssert.Contains(registrationSource, "\"outbox.insert\"");
        StringAssert.Contains(registrationSource, "\"messaging.outbox.append\"");
        StringAssert.Contains(
            executionSource,
            "DapperAotStaticCommandPlanRegistry.TryGetFactory");
        StringAssert.Contains(
            executionSource,
            "ReferenceEquals(expandedParameters, parameters)");
    }

    [TestMethod]
    public void DapperInfrastructure_UsesAotSafeSqlParameters()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var dapperDirectory = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper");
        var offenders = new[]
            {
                Path.Combine(dapperDirectory, "Outbox", "DapperOutboxStore.cs"),
                Path.Combine(dapperDirectory, "DapperDatabaseSessionLock.cs"),
                Path.Combine(
                    dapperDirectory,
                    "Outbox",
                    "DapperEventDeliveryProducerFencePositionReader.cs"),
            }
            .Where(path => ContainsAnonymousSqlParameterObject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "Native AOT Dapper 基础设施不得向 Host.Api SQL 执行器传递匿名参数："
                + string.Join(", ", offenders));
    }

    [TestMethod]
    public void DapperInfrastructure_RegistersHostApiRowMaterializers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var registrationSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.Dapper",
            "DapperAotInfrastructureRegistration.cs"));

        foreach (var recordType in new[]
                 {
                     "OutboxStreamCutoffSnapshot",
                     "DapperOutboxStore.OutboxRow",
                     "DapperOutboxStore.SqlServerBacklogRow",
                     "DapperOutboxStore.MySqlBacklogRow",
                     "DapperOutboxStore.SqlServerVersionRetirementRow",
                     "DapperOutboxStore.MySqlVersionRetirementRow",
                     "DapperOutboxStore.MySqlOutboxRow",
                     "DapperEventDeliveryProducerFencePositionReader.RollbackPreparationRow",
                     "DapperEventDeliveryProducerFencePositionReader.LastOutboxEventRow",
                     "DapperEventDeliveryProducerFencePositionReader.MySqlMasterStatusRow",
                     "DapperEventDeliveryProducerFencePositionReader.SqlServerMaxLsnRow",
                 })
        {
            StringAssert.Contains(
                registrationSource,
                $"Register<{recordType}>");
        }

        StringAssert.Contains(
            registrationSource,
            "Register<DapperOutboxStore.OutboxAcquireParameters>");
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
