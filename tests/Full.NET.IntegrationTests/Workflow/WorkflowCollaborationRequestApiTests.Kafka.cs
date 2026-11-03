extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Testcontainers.MsSql;
using LegacyWorker = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_physical_kafka_with_mysql() => PhysicalKafkaMySqlAsync(false);

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_cross_stream_late_rest_with_mysql() => PhysicalKafkaMySqlAsync(false, crossStream: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_physical_kafka_with_mysql() => PhysicalKafkaMySqlAsync(true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_cutover_lost_session_with_mysql() => PhysicalKafkaMySqlAsync(true, cutoverSessionLoss: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_kafka_combinations_with_mysql() => PhysicalKafkaMySqlAsync(true, combinations: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_kafka_reconciliation_with_mysql() => PhysicalKafkaMySqlAsync(true, reconciliation: true);

    private static async Task PhysicalKafkaMySqlAsync(bool native, bool combinations = false, bool reconciliation = false, bool crossStream = false, bool cutoverSessionLoss = false)
    {
        var connection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        try { await PhysicalKafkaAsync(DatabaseProvider.MySql, connection, native, combinations, reconciliation, crossStream: crossStream, cutoverSessionLoss: cutoverSessionLoss); }
        finally
        {
            // 只回收本例创建的随机库；不清理共享服务或其它会话资源。
            var owned = new MySqlConnectionStringBuilder(connection);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(owned.Database, "^fullnet_it_[a-f0-9]{32}$"));
            owned.UserID = "root"; owned.Password = SharedDatabaseFixture.MySqlRootPassword;
            await using var db = new MySqlConnection(owned.ConnectionString);
            MySqlConnection.ClearPool(db);
            await db.ExecuteAsync($"DROP DATABASE `{owned.Database}`");
        }
    }

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_physical_kafka_with_sql_server() => PhysicalKafkaSqlServerAsync(false);

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_sql_server_partial_transaction_cdc_position() => PhysicalKafkaSqlServerAsync(false, positionProbe: true);

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_sql_server_idle_heartbeat_advances_position() => PhysicalKafkaSqlServerAsync(false, idleProbe: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_sql_server_idle_heartbeat_advances_position() => PhysicalKafkaSqlServerAsync(true, idleProbe: true);

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_sql_server_autonomous_idle_heartbeat_advances_position() => PhysicalKafkaSqlServerAsync(false, autonomousIdleProbe: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_sql_server_autonomous_idle_heartbeat_advances_position() => PhysicalKafkaSqlServerAsync(true, autonomousIdleProbe: true);

    [TestMethod, TestCategory("WorkflowPhysicalKafka"), DoNotParallelize]
    public Task Collaboration_cross_stream_late_rest_with_sql_server() => PhysicalKafkaSqlServerAsync(false, crossStream: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_physical_kafka_with_sql_server() => PhysicalKafkaSqlServerAsync(true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_cutover_lost_session_with_sql_server() => PhysicalKafkaSqlServerAsync(true, cutoverSessionLoss: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_kafka_combinations_with_sql_server() => PhysicalKafkaSqlServerAsync(true, combinations: true);

    [TestMethod, TestCategory("WorkflowNativeKafka"), DoNotParallelize]
    public Task Collaboration_native_kafka_reconciliation_with_sql_server() => PhysicalKafkaSqlServerAsync(true, reconciliation: true);

    private static async Task PhysicalKafkaSqlServerAsync(bool native, bool combinations = false, bool reconciliation = false, bool positionProbe = false, bool crossStream = false, bool idleProbe = false, bool autonomousIdleProbe = false, bool cutoverSessionLoss = false)
    {
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(p =>
            {
                var limits = p.HostConfig ??= new();
                limits.Memory = 3L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 2_000_000_000;
                if (SharedDatabaseFixture.BoundedContainers) SharedDatabaseFixture.ApplyBoundedOwnership(p.Labels ??= new Dictionary<string, string>());
            }).Build();
        await server.StartAsync();
        var connection = new SqlConnectionStringBuilder(server.GetConnectionString());
        if (autonomousIdleProbe) await VerifyHeartbeatCaptureMismatchAsync(connection.ConnectionString);
        await using (var admin = new SqlConnection(connection.ConnectionString))
        {
            connection.InitialCatalog = "fullnet_workflow_kafka_" + Guid.NewGuid().ToString("N");
            await admin.ExecuteAsync($"CREATE DATABASE [{connection.InitialCatalog}]");
        }
        await PhysicalKafkaAsync(DatabaseProvider.SqlServer, connection.ConnectionString, native, combinations, reconciliation, positionProbe, crossStream, idleProbe, autonomousIdleProbe, cutoverSessionLoss);
    }

    /// <summary>真实业务写入、受控影子证明和切流；不直接写所有权、样本或Broker就绪事实。</summary>
    private static async Task PhysicalKafkaAsync(DatabaseProvider provider, string connection, bool native = false, bool combinations = false, bool reconciliation = false, bool positionProbe = false, bool crossStream = false, bool idleProbe = false, bool autonomousIdleProbe = false, bool cutoverSessionLoss = false)
    {
        var soakText = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_REAL_BUSINESS_SOAK_SECONDS");
        var ttlText = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_REAL_BUSINESS_TTL");
        var ttlTarget = string.IsNullOrWhiteSpace(ttlText) ? null : ttlText;
        var soakSeconds = 0;
        if (!string.IsNullOrWhiteSpace(soakText)
            && (!native || combinations || reconciliation || positionProbe || crossStream || idleProbe || autonomousIdleProbe || cutoverSessionLoss
                || !int.TryParse(soakText, out soakSeconds) || soakSeconds is not (60 or 3600)))
            throw new InvalidOperationException("真实业务多流长稳只允许独立 Native 双 Worker 的60秒预检或3600秒验收。");
        if (!string.IsNullOrWhiteSpace(ttlTarget) && (soakSeconds == 0 || ttlTarget is not ("formal" or "retry")))
            throw new InvalidOperationException("真实业务多流 TTL 只允许结合 Native 长稳配置选择 formal 或 retry。");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FULLNET_KAFKA_STREAM_FILTER_JAR")))
            throw new InvalidOperationException("真实Kafka验收需要经摘要校验的流过滤插件。");
        var nativeRoot = native ? Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_NATIVE_ROOT")
            ?? throw new InvalidOperationException("本批Native Kafka发布根目录缺失。") : null;
        var nativeApi = native ? await NativePublicationAsync(nativeRoot!, "latest.json", "Full.NET.Host.Api") : null;
        var nativeWorker = native ? await NativePublicationAsync(nativeRoot!, "latest-worker.json", "Full.NET.Host.Worker") : null;
        var data = native ? Directory.CreateDirectory(Path.Combine(nativeRoot!, "runtime-kafka-" + provider + "-" + Guid.CreateVersion7().ToString("N"))).FullName : null;
        var streams = WorkflowCollaborationKafkaRegistration.Streams;
        var group = WorkflowCollaborationKafkaRegistration.Consumer;
        using var exceptionProbe = new KafkaDispatchExceptionProbe();
        var ownershipMigration = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), $"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/{provider}/348_WorkflowCollaborationKafkaOwnership.sql"));
        await using var wire = positionProbe ? new MessagingKafkaFaultProxy(dockerReachable: true) : null;
        await using var pipeline = await CdcDebeziumPipelineEnvironment.StartAsync(advertisedHostPort: wire?.Port,
            dockerReachableProxy: native || positionProbe, automaticTtl: ttlTarget is not null, compactResources: true, connectProxyPort: wire?.Port,
            onKafkaStarted: wire is null ? null : port => wire.UpstreamPort = port);
        await using var delayedControl = reconciliation || crossStream || ttlTarget is not null ? await MessagingHttpFaultProxy.StartAsync(pipeline.ConnectBaseUri, dockerReachable: true) : null;
        var controlAddress = delayedControl?.Address ?? pipeline.ConnectBaseUri;
        using var connect = pipeline.CreateConnectAdminClient();
        var prefix = "fullnet-workflow-" + Guid.NewGuid().ToString("N");
        var formal = prefix + "-formal"; var shadow = prefix + "-shadow";
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migrations.Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var sourceDatabase = provider == DatabaseProvider.SqlServer ? new SqlConnectionStringBuilder(connection).InitialCatalog : new MySqlConnectionStringBuilder(connection).Database;
        var linuxConnection = provider == DatabaseProvider.SqlServer ? new SqlConnectionStringBuilder(connection) {
            DataSource = "host.docker.internal" + new SqlConnectionStringBuilder(connection).DataSource[new SqlConnectionStringBuilder(connection).DataSource.LastIndexOf(',')..] }.ConnectionString
            : new MySqlConnectionStringBuilder(connection) { Server = "host.docker.internal" }.ConnectionString;
        using var broker = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = pipeline.BootstrapServers }).Build();
        var ttlTopic = ttlTarget is null ? null : ttlTarget == "formal" ? streams[2].Topic
            : KafkaTopicNames.GetRetryTopic(streams[2].Topic, new KafkaMessagingOptions().RetryStages[0]);
        foreach (var topic in streams.SelectMany(s => new[] { s.Topic, "fullnet.dev.shadow." + s.Topic }).Concat(ttlTarget == "retry" ? [ttlTopic!] : []))
            await broker.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1,
                Configs = topic == ttlTopic ? new Dictionary<string, string> { ["cleanup.policy"] = "delete", ["retention.ms"] = "-1",
                    ["retention.bytes"] = "-1", ["segment.ms"] = "1000", ["segment.jitter.ms"] = "0", ["message.timestamp.type"] = "LogAppendTime" } : [] }]);
        Assert.IsTrue(await connect.WaitUntilReadyAsync(TimeSpan.FromSeconds(90)));
        Guid tenantId = default, memberId = default; string? memberName = null; Asset? sourceAsset = null;
        // 单流试点策略保持由服务器配置控制：每次重启真实API/Worker后采集一条流，再持久切流。
        for (var phase = 0; phase <= streams.Length; phase++)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:O} Workflow Kafka {provider} phase={phase}");
            var settings = Settings(Math.Min(phase, streams.Length - 1));
            if (soakSeconds > 0)
            {
                settings["Messaging:Kafka:PartitionBufferHighWatermark"] = "8";
                settings["Messaging:Kafka:PartitionBufferLowWatermark"] = "0";
                settings["Messaging:Kafka:PartitionKeyConcurrencySlots"] = "1";
                settings["Messaging:Kafka:OffsetCommitMode"] = "PerMessage";
                // 隔离的新消费组使用均衡策略，避免 Range 将每条单分区 Topic 都分给同一成员。
                settings["Messaging:Kafka:ClassicPartitionAssignment"] = "CooperativeSticky";
                settings["Messaging:Kafka:CooperativeStickyMigrationCompleted"] = "true";
                if (ttlTarget is not null) settings["Messaging:KafkaReplay:Enabled"] = "true";
            }
            if (native) { settings["DataProtection:KeyRingPath"] = Path.Combine(data!, "keys"); settings["Files:Local:RootPath"] = Path.Combine(data!, "files"); }
            using var factory = new FullNetApiFactory(provider, connection, settings);
            await factory.InitializeAsync(useSchemaTemplate: false);
            using var apiOutput = native ? Stream.Synchronized(File.Create(Path.Combine(data!, $"api-{phase}-pump.log"))) : null;
            await using var apiHost = native ? NativeHost(nativeApi!, provider, linuxConnection, data!, false, NativeSettings(settings), apiOutput) : null;
            if (apiHost is not null) await StartNativeAsync(apiHost, Path.Combine(data!, $"api-{phase}-startup.log"));
            try
            {
            using var source = apiHost is null ? factory.CreateClientForHost("localhost") : NativeClient(apiHost);
            if (apiHost is null) source.DefaultRequestHeaders.Add("Origin", "http://localhost");
            source.Timeout = TimeSpan.FromMinutes(crossStream ? 15 : 5);
            var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(source, "admin");
            source.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);
            using var target = apiHost is null ? factory.CreateClientForHost("localhost") : NativeClient(apiHost);
            if (apiHost is null) target.DefaultRequestHeaders.Add("Origin", "http://localhost");
            target.Timeout = TimeSpan.FromMinutes(2);
            if (phase == 0)
            {
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_cdc_heartbeat"), "352 迁移必须建立唯一心跳基线。");
                var heartbeatMigration = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(),
                    $"src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/{provider}/352_MessagingCdcHeartbeat.sql"));
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(heartbeatMigration, @"(?im)^\s*GO\s*$").Where(batch => !string.IsNullOrWhiteSpace(batch)))
                    await db.ExecuteAsync(batch);
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_cdc_heartbeat"), "352 重放不得产生重复基线。");
                if (autonomousIdleProbe)
                {
                    // 在本例随机数据库验证心跳的最小列权限，不能用管理员连接代替授权证据。
                    await db.ExecuteAsync("CREATE USER workflow_heartbeat_probe WITHOUT LOGIN; GRANT UPDATE (UpdatedAtUtc) ON dbo.fn_messaging_cdc_heartbeat TO workflow_heartbeat_probe;");
                    await db.OpenAsync();
                    try
                    {
                        const string updateHeartbeat = "UPDATE dbo.fn_messaging_cdc_heartbeat SET UpdatedAtUtc=SYSUTCDATETIME() WHERE Id='01941f29-7c00-7000-8000-000000000001'";
                        await db.ExecuteAsync("EXECUTE AS USER = 'workflow_heartbeat_probe'");
                        try
                        {
                            var denied = await Assert.ThrowsAsync<SqlException>(async () => await db.ExecuteAsync(updateHeartbeat));
                            Assert.AreEqual(229, denied.Number, "缺少 Id 读取权限时应拒绝心跳更新。");
                        }
                        finally { await db.ExecuteAsync("REVERT"); }
                        await db.ExecuteAsync("GRANT SELECT (Id) ON dbo.fn_messaging_cdc_heartbeat TO workflow_heartbeat_probe");
                        await db.ExecuteAsync("EXECUTE AS USER = 'workflow_heartbeat_probe'");
                        try { Assert.AreEqual(1, await db.ExecuteAsync(updateHeartbeat), "最小列权限应允许固定心跳更新。"); }
                        finally { await db.ExecuteAsync("REVERT"); }
                    }
                    finally { await db.CloseAsync(); }
                }
                foreach (var stream in streams)
                    Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND TopicCode=@topic AND CurrentOwner=0 AND PreviousOwner=0 AND CdcSourcePositionJson IS NULL AND CdcKafkaBindingJson IS NULL", new { type = stream.EventType, topic = stream.Topic }), "正式迁移必须登记安全Legacy基线");
                // 仅在本例随机库且尚无业务写入时模拟缺一条登记，验证部分完成可重跑且已有行不被改写。
                // 保留SQL Server的DateTimeOffset与MySQL的DateTime原值，重放检查不做有损转换。
                var retainedTime = await db.ExecuteScalarAsync<object>("SELECT UpdatedAtUtc FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = streams[0].EventType });
                Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND CurrentOwner=0 AND PreviousOwner=0 AND CdcKafkaBindingJson IS NULL", new { type = streams[^1].EventType }));
                await db.ExecuteAsync(ownershipMigration);
                Assert.AreEqual(retainedTime, await db.ExecuteScalarAsync<object>("SELECT UpdatedAtUtc FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = streams[0].EventType }));
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND CurrentOwner=0", new { type = streams[^1].EventType }));
                await using var catalogScope = factory.Services.CreateAsyncScope();
                var catalog = catalogScope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
                var consumed = catalogScope.ServiceProvider.GetServices<IIntegrationEventSubscription>()
                    .Select(s => catalog.GetTopicRequired(s.EventType, s.SchemaVersion).TopicCode).Distinct().ToArray();
                var retries = new KafkaMessagingOptions().RetryStages;
                var topics = consumed.SelectMany(topic => new[] { topic, KafkaTopicNames.GetDeadLetterTopic(topic) }
                    .Concat(retries.Select(stage => KafkaTopicNames.GetRetryTopic(topic, stage)))).Distinct();
                try { await broker.CreateTopicsAsync(topics.Select(topic => new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 })); }
                catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError)) { }
                tenantId = (await source.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(t => t.Identifier == "acme").Id;
                memberName = "kafka.workflow." + Guid.NewGuid().ToString("N");
                var memberPermissions = new[] { WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
                    WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.TodosRead,
                    WorkflowPermissions.TodosApprove, WorkflowPermissions.CollaborationGrantsAccept, "tenancy.tenants.switch" };
                if (combinations) memberPermissions = WorkflowPermissions.All.Concat(["tenancy.tenants.switch"]).ToArray();
                var member = await factory.CreateHostIdentityAsync(memberName, memberPermissions, password: FullNetApiFactory.TestPassword);
                memberId = member.UserId;
                var email = memberName + "@example.com";
                await factory.EnsureHostUserProfileEmailAsync(memberId, email, memberName);
                var ownerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, "admin");
                target.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target, ownerToken, tenantId));
                var invitation = await PostAsync(target, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                // Host登录必须使用匿名Host上下文，不能沿用发出租户邀请时的租户Bearer。
                target.DefaultRequestHeaders.Authorization = null;
                target.DefaultRequestHeaders.Authorization = new("Bearer", native ? await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, memberName) : member.AccessToken);
                await PostAsync(target, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
            }
            target.DefaultRequestHeaders.Authorization = null;
            var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, memberName!);
            target.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target, memberToken, tenantId));
            if (phase == 0)
            {
                var targetAsset = await AssetAsync(target, approval: true); sourceAsset = await AssetAsync(source);
                var proposal = await PostAsync(source, "/api/v1/workflow/collaboration-grants", new { targetTenantId = tenantId, targetScopeKey = "tenant", idempotencyKey = prefix,
                    offer = new { schemaVersion = 1, sourceDefinitionId = sourceAsset.Definition, sourceFormVersionId = sourceAsset.Form, targetWorkflowKey = targetAsset.Key,
                        inputFieldKeys = new[] { "title" }, outputFieldKeys = new[] { "answer" }, maximumTargetActivations = 64,
                        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(soakSeconds > 0 ? 3 : 1) } });
                var grant = proposal.GetProperty("id").GetGuid();
                var accepted = await PostAsync(target, $"/api/v1/workflow/collaboration-grants/{grant:D}/accept", new { expectedRevision = 1, idempotencyKey = prefix,
                    acceptance = new { schemaVersion = 1, targetDefinitionVersionId = targetAsset.Version, starterUserId = memberId, maximumActivities = 32, allowedUserIds = new[] { memberId },
                        inputFields = new { title = "title" }, outputFields = new { title = "answer" } } });
                // 首次发布也会占用草稿修订号，后续编辑必须使用当前API返回的修订。
                var sourceDefinition = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{sourceAsset.Definition:D}");
                using var saved = await source.PutAsJsonAsync($"/api/v1/workflow/definitions/{sourceAsset.Definition:D}/draft", new { expectedRevision = sourceDefinition.GetProperty("draftRevision").GetInt64(),
                    draft = new { schemaVersion = 1, workflowKindKey = "automation", nodes = new object[] {
                        new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "collaborate" } } },
                        new { nodeKey = "collaborate", nodeTypeKey = "process.collaboration", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" }, collaborationPolicy = new { schemaVersion = 1, grantId = grant, grantRevision = 2, policyHash = accepted.GetProperty("policyHash").GetString() } } },
                        new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } } });
                Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
                var savedDefinition = await saved.Content.ReadFromJsonAsync<JsonElement>();
                sourceAsset = sourceAsset with { Version = (await PostAsync(source, $"/api/v1/workflow/definitions/{sourceAsset.Definition:D}/publish", new { expectedRevision = savedDefinition.GetProperty("draftRevision").GetInt64(), formVersionId = sourceAsset.Form })).GetProperty("id").GetGuid() };
                if (provider == DatabaseProvider.SqlServer)
                {
                    var cdc = await SqlServerCdcTestSupport.TryEnableCdcAsync(connection); Assert.IsTrue(cdc.Succeeded, cdc.FailureReason);
                    await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "deploy/messaging/sqlserver/enable-shadow-cdc.sql")));
                    await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "deploy/messaging/sqlserver/enable-heartbeat-cdc.sql")));
                }
                for (var index = 0; index < streams.Length; index++)
                foreach (var (name, append) in new[] { (formal + "-" + index, true), (shadow + "-" + index, false) })
                {
                    await connect.RegisterConnectorAsync(name, await ConnectorAsync(name, append, streams[index].Topic));
                    if (!combinations && !reconciliation)
                    {
                        Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(120)), "真实Connector未就绪");
                        await WaitKafkaAsync(async () => await connect.TryReadConnectorPositionAsync(name) is not null, "Connector初始源位置");
                    }
                }
                if (combinations || reconciliation)
                {
                    // 先完成全部配置，避免逐项等待期间的新注册反复打断先前任务的启动。
                    for (var index = 0; index < streams.Length; index++)
                    foreach (var name in new[] { formal + "-" + index, shadow + "-" + index })
                        await WaitNativeConnectorBootstrapAsync(pipeline.ConnectBaseUri, connect, name, data!);
                }
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            using var worker = native ? null : await IntegrationWorkerHostFactory.BuildAsync(provider, connection, settings, "Full.NET.Workflow.PhysicalKafka", nameof(WorkflowCollaborationHostedProcessor),
                additionalHostedServiceNames: [nameof(KafkaConsumerWorker), nameof(LegacyWorker.OutboxProcessor), nameof(WorkflowRecoveryHostedProcessor)], configureServices: services =>
                {
                    // 与生产Worker的HybridKafka入口一致，先注册Inbox事务调度核心再注册Kafka。
                    services.AddFullNetModularity();
                    services.AddFullNetKafkaMessaging(config, "Testing");
                    services.AddOptions<LegacyWorker.OutboxWorkerOptions>().Configure(o => { o.PollMilliseconds = 100; o.MaximumIdlePollMilliseconds = 100; o.BatchSize = 20; });
                    services.AddHostedService<LegacyWorker.OutboxProcessor>();
                });
            if (worker is not null) await using (var consumerScope = worker.Services.CreateAsyncScope())
                Assert.IsNotNull(consumerScope.ServiceProvider.GetRequiredService<IntegrationEventConsumerDispatcher>());
            await using var monitor = combinations && phase == streams.Length
                ? await NativeKafkaMonitor.StartAsync(Path.Combine(data!, "monitor")) : null;
            var workerSettings = native ? NativeSettings(settings) : null;
            if (monitor is not null) foreach (var setting in monitor.Settings) workerSettings![setting.Key] = setting.Value;
            using var workerOutput = native ? Stream.Synchronized(File.Create(Path.Combine(data!, $"worker-{phase}-pump.log"))) : null;
            await using var workerHost = native ? NativeHost(nativeWorker!, provider, linuxConnection, data!, true, workerSettings, workerOutput) : null;
            var peerSettings = soakSeconds > 0 && phase == streams.Length ? new Dictionary<string, string>(workerSettings!) : null;
            if (peerSettings is not null) peerSettings["Messaging__Kafka__ClientId"] = "fullnet.workflow.physical.worker.peer";
            using var soakPeerOutput = peerSettings is not null ? Stream.Synchronized(File.Create(Path.Combine(data!, "worker-soak-peer-pump.log"))) : null;
            await using var soakPeer = peerSettings is not null ? NativeHost(nativeWorker!, provider, linuxConnection, data!, true, peerSettings, soakPeerOutput) : null;
            if (worker is not null) await worker.StartAsync();
            else await StartNativeAsync(workerHost!, Path.Combine(data!, $"worker-{phase}-startup.log"));
            if (soakPeer is not null) await StartNativeAsync(soakPeer, Path.Combine(data!, "worker-soak-peer-startup.log"));
            Guid fencedSource = default, fencedCall = default, fencedTarget = default;
            try
            {
                await WaitKafkaAsync(async () =>
                {
                    try { return (await broker.DescribeConsumerGroupsAsync([group])).ConsumerGroupDescriptions.Single().State == ConsumerGroupState.Stable; }
                    catch (DescribeConsumerGroupsException) { return false; }
                }, "真实协作消费组");
                if (phase < streams.Length)
                    await PostAsync(source, "/api/v1/messaging/delivery/shadow-captures/", new StartShadowCaptureRequest(Guid.CreateVersion7(), null, streams[phase].EventType, 1, 1, 300, "Workflow physical Kafka proof"));
                await RunAsync(cancel: phase == 3, pauseBroker: phase == streams.Length);
                if (phase < streams.Length)
                {
                    var stream = streams[phase];
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@type", new { type = stream.EventType }), "Legacy影子样本不能混入正式追加表");
                    await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event WHERE MessageType=@type", new { type = stream.EventType }) == 1, "真实业务影子样本");
                    var sampleId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_messaging_shadow_event WHERE MessageType=@type", new { type = stream.EventType });
                    using (var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
                        GroupId = prefix + ".shadow-ready." + phase, AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false }).Build())
                    {
                        // 源事务提交不代表异步CDC已经到达Broker；等实际EventId后仍由原API核对完整证明。
                        observer.Subscribe("fullnet.dev.shadow." + stream.Topic);
                        await WaitKafkaAsync(async () =>
                        {
                            var record = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(1)));
                            if (record is null) return false;
                            Assert.IsTrue(new KafkaEnvelopeReader().TryRead(record, out var envelope, out var code), "真实影子信封解析失败：" + code);
                            Assert.IsNotNull(envelope);
                            return envelope.EventId == sampleId;
                        }, "真实Broker影子样本");
                    }
                    await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type AND ProcessedAtUtc IS NULL", new { type = stream.EventType }) == 0, "切流前Legacy积压排空");
                    await PostAsync(source, "/api/v1/messaging/delivery/cutover-evidence", new VerifyCutoverRequest(stream.EventType, 1, "Workflow real source/broker/group"));
                    if (cutoverSessionLoss && phase == 0)
                        await VerifyNativeCutoverSessionLossAsync(provider, connection, source, stream.EventType);
                    await PostAsync(source, "/api/v1/messaging/delivery/cutover", new ChangeDeliveryOwnerRequest(stream.EventType, 1, EventDeliveryOwner.CdcKafka, "Workflow physical acceptance"));
                    if (cutoverSessionLoss && phase == 0)
                    {
                        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = stream.EventType }));
                        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit WHERE ActionKey='messaging.delivery.cutover'"), "仅接管后的成功切流保留审计。");
                    }
                }
                else
                {
                    await RunAsync(cancel: true);
                    foreach (var stream in streams)
                    {
                        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@type", new { type = stream.EventType }) > 0);
                        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type", new { group, type = stream.EventType }) > 0, "每条协作流必须真实进入Inbox");
                    }
                    if (soakSeconds > 0)
                    {
                        var activeSoakPeer = soakPeer ?? throw new InvalidOperationException("真实多流长稳缺少第二个 Native Worker。");
                        var nextSessionRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
                        await RunRealBusinessStreamsSoakAsync(db, broker, connect, streams, group, formal, data!, soakSeconds,
                            async cancel =>
                            {
                                if (DateTimeOffset.UtcNow >= nextSessionRefresh)
                                {
                                    source.DefaultRequestHeaders.Authorization = null;
                                    source.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(source, "admin"));
                                    target.DefaultRequestHeaders.Authorization = null;
                                    var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, memberName!);
                                    target.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target, token, tenantId));
                                    nextSessionRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
                                }
                                await RunAsync(cancel);
                            }, async () =>
                            {
                                await StopNativeAsync(activeSoakPeer, Path.Combine(data!, "worker-soak-peer-restart.log"));
                                try
                                {
                                    await WaitKafkaAsync(async () =>
                                    {
                                        var state = (await broker.DescribeConsumerGroupsAsync([group])).ConsumerGroupDescriptions.Single();
                                        return state.State == ConsumerGroupState.Stable && state.Members.Count == 1;
                                    }, "单 Worker 故障切换时真实消费组稳定");
                                    await RunAsync(cancel: true);
                                }
                                finally { await StartNativeAsync(activeSoakPeer, Path.Combine(data!, "worker-soak-peer-restarted.log")); }
                            }, () => [workerHost!.Id, activeSoakPeer.Id]);
                        if (ttlTarget is not null)
                        {
                            Assert.IsNotNull(delayedControl);
                            await RunRealBusinessStreamTtlAsync(db, broker, connect, delayedControl, source, streams, group,
                                formal, pipeline.BootstrapServers, ttlTarget, data!, () => RunAsync(cancel: true),
                                PrepareFencedCompletionAsync, AssertFencedCompletionAsync);
                        }
                        continue;
                    }
                    if (crossStream)
                    {
                        await VerifyCrossStreamAsync();
                        continue;
                    }
                    var origins = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin");
                    var controls = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control");
                    foreach (var stream in streams)
                    {
                        // 重投实际Broker信封，保留原Id和Headers；等生产消费组确认新Offset后再断言幂等。
                        using var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
                            GroupId = prefix + ".observe." + stream.Topic, AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false }).Build();
                        observer.Subscribe(stream.Topic);
                        var message = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(60)));
                        Assert.IsNotNull(message, "正式Topic必须有真实CDC信封");
                        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = pipeline.BootstrapServers, EnableIdempotence = true }).Build();
                        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group", new { group });
                        var sent = await producer.ProduceAsync(stream.Topic, new Message<string, byte[]> { Key = message.Message.Key, Value = message.Message.Value, Headers = message.Message.Headers });
                        await WaitKafkaAsync(async () => (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group,
                            [new TopicPartition(stream.Topic, sent.Partition)])])).Single().Partitions.Single().Offset.Value > sent.Offset.Value, "重复信封真实Offset确认");
                        Assert.AreEqual(count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group", new { group }));
                    }
                    Assert.AreEqual(origins, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin"));
                    Assert.AreEqual(controls, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control"));
                    // 未记账重放只能补缺；不能把已切流的四条所有权和实际资源基线重置为Legacy。
                    var baselines = new string?[streams.Length];
                    for (var index = 0; index < streams.Length; index++)
                        baselines[index] = await db.ExecuteScalarAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND CurrentOwner=2", new { type = streams[index].EventType });
                    await db.ExecuteAsync(ownershipMigration);
                    for (var index = 0; index < streams.Length; index++)
                    {
                        Assert.IsFalse(string.IsNullOrEmpty(baselines[index]));
                        Assert.AreEqual(baselines[index], await db.ExecuteScalarAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1 AND CurrentOwner=2", new { type = streams[index].EventType }));
                    }
                    if (combinations)
                    {
                        // 四流均为CDC时验证本地Files、两层子代和并行目标；先停止主Worker，保留真实停机/续办边界。
                        await StopNativeAsync(workerHost!, Path.Combine(data!, "worker-before-combinations.log"));
                        var hostUser = await source.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me"); Assert.IsNotNull(hostUser);
                        var counts = new long[streams.Length];
                        for (var index = 0; index < streams.Length; index++) counts[index] = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type", new { group, type = streams[index].EventType });
                        var nativeSettings = NativeSettings(settings);
                        await RefreshCombinationSessionsAsync();
                        await NativeCollaborationAsync(target, source, db, hostUser.Id, null, nativeWorker!, provider, linuxConnection, data!, true, "success", storageSettings: nativeSettings);
                        await RefreshCombinationSessionsAsync();
                        await NativeCollaborationAsync(source, target, db, memberId, tenantId, nativeWorker!, provider, linuxConnection, data!, false, "success", storageSettings: nativeSettings);
                        await RefreshCombinationSessionsAsync();
                        await NativeCollaborationAsync(source, target, db, memberId, tenantId, nativeWorker!, provider, linuxConnection, data!, true, "cancel", storageSettings: nativeSettings);
                        await RefreshCombinationSessionsAsync();
                        await NativeCollaborationAsync(source, target, db, memberId, tenantId, nativeWorker!, provider, linuxConnection, data!, false, "revoke", storageSettings: nativeSettings);
                        for (var index = 0; index < streams.Length; index++)
                            Assert.IsTrue(await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type", new { group, type = streams[index].EventType }) > counts[index], "扩展组合必须继续经过四条真实Kafka流。");
                        await StartNativeAsync(workerHost!, Path.Combine(data!, "worker-after-combinations-startup.log"));
                        await WaitKafkaAsync(async () => (await broker.DescribeConsumerGroupsAsync([group])).ConsumerGroupDescriptions.Single().State == ConsumerGroupState.Stable, "扩展组合后恢复原生消费组");
                        await using (var monitoringControl = factory.Services.CreateAsyncScope())
                        {
                            // 故障注入持有真实运维控制会话，避免自动纠偏与两分钟告警窗口竞争；不改持久所有权或指标。
                            monitoringControl.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Tenancy.ICurrentTenantContextWriter>().SetHost();
                            await using var controlLock = await monitoringControl.ServiceProvider.GetRequiredService<IDatabaseSessionLock>()
                                .TryAcquireAsync(EventDeliveryControlResources.SessionLock);
                            Assert.IsNotNull(controlLock, "告警故障注入必须取得实际控制会话。");
                            await monitor!.VerifyAsync(connect, formal + "-2", streams.Select(stream => stream.Topic).ToArray(),
                                (controlLock as IDatabaseSessionLockLease ?? throw new InvalidOperationException("控制会话缺少持锁校验。")));
                        }
                        // 长批次仍经真实登录续会话；不修改令牌寿命、撤销策略或受保护业务校验。
                        await RefreshCombinationSessionsAsync();

                        async Task RefreshCombinationSessionsAsync()
                        {
                            // 每个租约恢复场景可能跨越默认10分钟会话边界，两侧均重新登录并恢复原作用域。
                            source.DefaultRequestHeaders.Authorization = null;
                            source.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(source, "admin"));
                            target.DefaultRequestHeaders.Authorization = null;
                            var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, memberName!);
                            target.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target, token, tenantId));
                        }
                    }
                    if (reconciliation)
                    {
                        using var peerOutput = Stream.Synchronized(File.Create(Path.Combine(data!, "api-reconciliation-peer-pump.log")));
                        await using var peerHost = NativeHost(nativeApi!, provider, linuxConnection, data!, false, NativeSettings(settings), peerOutput);
                        await StartNativeAsync(peerHost, Path.Combine(data!, "api-reconciliation-peer-startup.log"));
                        try
                        {
                            using var peer = NativeClient(peerHost);
                            peer.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(peer, "admin"));
                            await VerifyNativeMultiStreamReconciliationAsync(source, peer, db, provider, connect, delayedControl!, workerHost!,
                                formal, streams.Select(stream => stream.EventType).ToArray(), Path.Combine(data!, "reconciliation.json"));
                        }
                        finally { await StopNativeAsync(peerHost, Path.Combine(data!, "api-reconciliation-peer.log")); }
                    }
                    else await PostAsync(source, "/api/v1/messaging/delivery/rollback", new ChangeDeliveryOwnerRequest(streams[0].EventType, 1, EventDeliveryOwner.LegacyPolling, "Workflow isolated rollback"));
                    for (var index = 1; index < streams.Length; index++)
                    {
                        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = streams[index].EventType }));
                        Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(formal + "-" + index, TimeSpan.FromSeconds(30)), "回退请求流不能暂停其它流的发布者");
                    }
                    await RunAsync(cancel: false);
                    await RunAsync(cancel: true);
                    // 部分位点回退必须由真实消费者排空；其余过滤探针仍保持原有停止顺序。
                    if (!positionProbe)
                    {
                        if (worker is not null) await worker.StopAsync();
                        else await StopNativeAsync(workerHost!, Path.Combine(data!, "worker-before-stream-filter-probe.log"));
                    }
                    var filterStream = streams[1];
                    using (var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
                        GroupId = prefix + ".stream-filter", EnableAutoCommit = false }).Build())
                    {
                        var partition = new TopicPartition(filterStream.Topic, new Partition(0));
                        var start = observer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High;
                        observer.Assign(new TopicPartitionOffset(partition, start));
                        var otherVersion = Guid.CreateVersion7(); var otherType = Guid.CreateVersion7(); var marker = Guid.CreateVersion7();
                        if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
                        await using var transaction = await db.BeginTransactionAsync();
                        const string copy = """
                            INSERT INTO fn_messaging_outbox_event (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                                CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
                            SELECT @id, @type, @version, ContentType, TenantId, PartitionKey,
                                CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc
                            FROM fn_messaging_outbox_event WHERE Id=@source
                            """;
                        var original = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_messaging_outbox_event WHERE MessageType=@type", new { type = filterStream.EventType }, transaction);
                        foreach (var row in new[] { (otherVersion, filterStream.EventType, 2), (otherType, "fullnet.workflow.filter.probe", 1), (marker, filterStream.EventType, 1) })
                            Assert.AreEqual(1, await db.ExecuteAsync(copy, new { id = row.Item1, type = row.Item2, version = row.Item3, source = original }, transaction));
                        await transaction.CommitAsync();
                        await WaitKafkaAsync(async () =>
                        {
                            var record = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(1)));
                            if (record is null) return false;
                            Assert.IsTrue(new KafkaEnvelopeReader().TryRead(record, out var envelope, out var code), "过滤探针信封无效：" + code);
                            Assert.IsNotNull(envelope);
                            Assert.AreNotEqual(otherVersion, envelope.EventId, "不同版本不得进入目标Topic。");
                            Assert.AreNotEqual(otherType, envelope.EventId, "不同类型不得进入目标Topic。");
                            return envelope.EventId == marker;
                        }, "真实Connector处理匹配及不匹配类型/版本");
                    }
                    if (positionProbe)
                        await VerifyPartialCdcPositionAsync(source, db, pipeline, connect, wire!, formal + "-1", filterStream.EventType, filterStream.Topic, prefix);
                    if (idleProbe)
                        await VerifyIdleHeartbeatPositionAsync(db, pipeline, connect, formal + "-1", prefix);
                    if (autonomousIdleProbe)
                        await VerifyAutonomousIdleHeartbeatAsync(db, pipeline, connect, formal + "-1", prefix,
                            Enumerable.Range(0, streams.Length).SelectMany(index => new[] { formal + "-" + index, shadow + "-" + index }));
                    Console.WriteLine($"{DateTimeOffset.UtcNow:O} Workflow Kafka {provider} four streams, pause/recovery, duplicate offsets and isolated rollback verified");
                }
            }
            catch
            {
                // 失败前留下最小真实状态，禁止输出业务正文、连接串或Connector配置。
                try { await DiagnoseAsync(); }
                catch (Exception diagnostic) { Console.WriteLine("Workflow Kafka diagnostic unavailable: " + diagnostic.GetType().Name); }
                throw;
            }
            finally
            {
                if (soakPeer is not null) await StopNativeAsync(soakPeer, Path.Combine(data!, "worker-soak-peer.log"));
                if (worker is not null) await worker.StopAsync();
                else await StopNativeAsync(workerHost!, Path.Combine(data!, $"worker-{phase}.log"));
            }

            async Task DiagnoseAsync()
            {
                foreach (var table in new[] { "call", "delivery", "admission", "control" })
                    Console.WriteLine("Workflow Kafka phase=" + phase + " " + table + " states=" + string.Join(',', await db.QueryAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_" + table)));
                Console.WriteLine("Workflow Kafka Inbox types=" + string.Join(',', await db.QueryAsync<string>("SELECT MessageType FROM fn_messaging_inbox_message WHERE ConsumerName=@group", new { group })));
                Console.WriteLine("Workflow Kafka Inbox errors=" + string.Join(',', await db.QueryAsync<string>("SELECT LastErrorCode FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND LastErrorCode IS NOT NULL", new { group })));
                Console.WriteLine("Workflow Kafka collaboration failures=" + string.Join(',', await db.QueryAsync<string>("SELECT Summary FROM fn_workflow_execution_log WHERE TransitionKey='node.collaboration.denied' OR TransitionKey='node.collaboration.closed'")));
                var denied = await db.QueryAsync("""
                    SELECT c.Id,c.InstanceId,c.StatusKey AS CallStatus,i.StatusKey AS InstanceStatus,
                        s.StatusKey AS StepStatus,e.StatusKey AS EpochStatus,a.StatusKey AS AdmissionStatus,
                        a.TargetInstanceId,g.StatusKey AS GrantStatus,g.ExpiresAtUtc
                    FROM fn_workflow_collaboration_call c
                    LEFT JOIN fn_workflow_instance i ON i.Id=c.InstanceId
                    LEFT JOIN fn_workflow_step s ON s.Id=c.Id
                    LEFT JOIN fn_workflow_execution_epoch e ON e.Id=c.ExecutionEpochId
                    LEFT JOIN fn_workflow_collaboration_admission a ON a.Id=c.Id
                    LEFT JOIN fn_workflow_collaboration_grant g ON g.Id=c.GrantId
                    WHERE c.StatusKey='denied'
                    """);
                foreach (var row in denied)
                    Console.WriteLine("Workflow Kafka denied facts=" + JsonSerializer.Serialize((IDictionary<string, object>)row));
                foreach (var stream in streams)
                {
                    Console.WriteLine("Workflow Kafka " + stream.EventType + " owner=" + await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = stream.EventType })
                        + " append=" + await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@type", new { type = stream.EventType }));
                }
                var selected = streams[Math.Min(phase, streams.Length - 1)].Topic;
                foreach (var topic in new[] { selected, "fullnet.dev.shadow." + selected, KafkaTopicNames.GetDeadLetterTopic(selected), KafkaTopicNames.GetRetryTopic(selected, new KafkaMessagingOptions().RetryStages[0]) })
                {
                    using var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
                        GroupId = prefix + ".diagnostic." + Guid.NewGuid().ToString("N"), AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false }).Build();
                    observer.Subscribe(topic);
                    var sample = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(5)));
                    if (sample is null) { Console.WriteLine("Workflow Kafka sample " + topic + " empty"); continue; }
                    var valid = new KafkaEnvelopeReader().TryRead(sample, out var envelope, out var code);
                    KafkaDeliveryHeaders.TryReadHeader(sample.Message.Headers, KafkaDeliveryHeaderNames.FailureCode, out var failure);
                    Console.WriteLine("Workflow Kafka sample " + topic + " valid=" + valid + " envelope=" + envelope?.MessageType + " parse=" + code + " failure=" + failure + " bytes=" + sample.Message.Value.Length);
                }
            }

            async Task VerifyCrossStreamAsync()
            {
                using var peer = factory.CreateClientForHost("localhost");
                peer.DefaultRequestHeaders.Add("Origin", "http://localhost");
                peer.Timeout = TimeSpan.FromMinutes(5);
                peer.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(peer, "admin"));
                var oldType = streams[1].EventType;
                var newType = streams[0].EventType;
                var oldChange = new ChangeDeliveryOwnerRequest(oldType, 1, EventDeliveryOwner.LegacyPolling, "cross-stream delayed request");
                var gate = delayedControl!.Arm("PUT", $"/connectors/{formal}-1/pause", afterExecution: false);
                gate.ContinueAfterClientDisconnect = true;
                var oldRollback = ControlAsync(source, "/api/v1/messaging/delivery/rollback", oldChange);
                try
                {
                    await ReachedAsync(gate, oldRollback);
                    var operation = (await peer.GetFromJsonAsync<RollbackOperationResponse>(
                        "/api/v1/messaging/delivery/rollback-operations?eventType=" + oldType + "&schemaVersion=1"))!;
                    var session = provider == DatabaseProvider.SqlServer
                        ? await db.QuerySingleAsync<int>("SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type='APPLICATION' AND request_owner_type='SESSION' AND resource_database_id=DB_ID() AND resource_description LIKE '%messaging.delivery_control%'")
                        : await db.QuerySingleAsync<int>("SELECT IS_USED_LOCK('messaging.delivery_control')");
                    Assert.IsGreaterThan(0, session);
                    await db.ExecuteAsync("KILL " + session.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    await PostAsync(peer, $"/api/v1/messaging/delivery/rollback-operations/{operation.Generation:D}/recover", new RecoverRollbackRequest("cross-stream recover old session"));
                    await PostAsync(peer, "/api/v1/messaging/delivery/rollback", oldChange);
                    await PostAsync(peer, "/api/v1/messaging/delivery/rollback",
                        new ChangeDeliveryOwnerRequest(newType, 1, EventDeliveryOwner.LegacyPolling, "cross-stream new owner preparation"));
                    var originalBinding = await db.ExecuteScalarAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = oldType });
                    Assert.IsFalse(string.IsNullOrWhiteSpace(originalBinding));

                    // 外部资源真实改配为另一流；持久历史归属仍必须阻止重新激活。
                    await connect.RegisterConnectorAsync(formal + "-1", await ConnectorAsync(formal + "-1", true, streams[0].Topic));
                    await connect.ResumeConnectorAsync(formal + "-1");
                    Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(formal + "-1", TimeSpan.FromSeconds(120)));
                    var crossSettings = Settings(0);
                    const string mapping = "Messaging:KafkaConnectRollback:Streams:";
                    var originalSettings = crossSettings.Where(item => item.Key.StartsWith(mapping, StringComparison.Ordinal)).ToArray();
                    foreach (var item in originalSettings) crossSettings.Remove(item.Key);
                    var kept = new[] { 0, 2, 3 };
                    for (var i = 0; i < kept.Length; i++)
                        foreach (var item in originalSettings.Where(item => item.Key.StartsWith(mapping + kept[i] + ":", StringComparison.Ordinal)))
                            crossSettings[mapping + i + ":" + item.Key[(mapping.Length + kept[i].ToString().Length + 1)..]] = item.Value;
                    crossSettings[mapping + "0:ConnectorName"] = formal + "-1";
                    using var crossFactory = new FullNetApiFactory(provider, connection, crossSettings);
                    await crossFactory.InitializeAsync(useSchemaTemplate: false);
                    using var crossApi = crossFactory.CreateClientForHost("localhost");
                    crossApi.DefaultRequestHeaders.Add("Origin", "http://localhost");
                    crossApi.Timeout = TimeSpan.FromMinutes(5);
                    crossApi.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(crossApi, "admin"));
                    var previousGeneration = await db.ExecuteScalarAsync<Guid?>("SELECT Generation FROM fn_messaging_shadow_capture WHERE MessageType=@type AND SchemaVersion=1", new { type = newType });
                    var capture = await PostAsync(crossApi, "/api/v1/messaging/delivery/shadow-captures/",
                        new StartShadowCaptureRequest(Guid.CreateVersion7(), previousGeneration, newType, 1, 1, 300, "cross-stream real shadow proof"));
                    var generation = capture.GetProperty("generation").GetGuid();
                    await RunAsync(cancel: false, startClient: crossApi);
                    await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_event WHERE CaptureGeneration=@generation AND MessageType=@type", new { generation, type = newType }) == 1,
                        "跨流重用尝试的真实业务影子记录");
                    var sample = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_messaging_shadow_event WHERE CaptureGeneration=@generation AND MessageType=@type", new { generation, type = newType });
                    using (var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
                        GroupId = prefix + ".cross-shadow", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false }).Build())
                    {
                        observer.Subscribe("fullnet.dev.shadow." + streams[0].Topic);
                        await WaitKafkaAsync(async () =>
                        {
                            var record = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(1)));
                            return record is not null && new KafkaEnvelopeReader().TryRead(record, out var envelope, out _) && envelope?.EventId == sample;
                        }, "跨流重用尝试的真实Broker影子样本");
                    }
                    await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@type AND ProcessedAtUtc IS NULL", new { type = newType }) == 0,
                        "跨流切换前目标Legacy积压排空");
                    await PostAsync(crossApi, "/api/v1/messaging/delivery/cutover-evidence", new VerifyCutoverRequest(newType, 1, "cross-stream real evidence"));
                    using (var denied = await crossApi.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", new ChangeDeliveryOwnerRequest(newType, 1, EventDeliveryOwner.CdcKafka, "cross-stream reused connector")))
                    {
                        var body = await denied.Content.ReadAsStringAsync();
                        Assert.IsFalse(denied.IsSuccessStatusCode, "历史Connector不得被另一流激活。");
                        StringAssert.Contains(body, "Active CDC streams require persisted, isolated connector and topic bindings.");
                    }
                    gate.Release.TrySetResult();
                    await gate.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    using (var oldResponse = await oldRollback) Assert.IsTrue((int)oldResponse.StatusCode >= 500, "失锁旧请求不能完成回退。");
                    await WaitKafkaAsync(() => connect.IsConnectorPausedAsync(formal + "-1"), "旧请求实际迟到并作用于同名Connector");
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = newType }));
                    Assert.AreEqual(originalBinding, await db.ExecuteScalarAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = oldType }));
                    for (var i = 2; i < streams.Length; i++)
                    {
                        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = streams[i].EventType }));
                        Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(formal + "-" + i, TimeSpan.FromSeconds(30)));
                    }
                }
                finally { gate.Release.TrySetResult(); }
            }

            async Task PrepareFencedCompletionAsync()
            {
                var key = Guid.NewGuid().ToString("N");
                var started = await PostAsync(source, "/api/v1/workflow/instances", new { definitionVersionId = sourceAsset!.Version,
                    businessType = "kafka.workflow", businessId = key, idempotencyKey = key,
                    initialValues = new { title = "shared", secret = "source-private" } }, HttpStatusCode.Created);
                fencedSource = started.GetProperty("id").GetGuid();
                await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_call WHERE InstanceId=@fencedSource AND StatusKey='accepted'", new { fencedSource }) == 1, "受保护发布前其他真实流已完成准入");
                fencedCall = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_collaboration_call WHERE InstanceId=@fencedSource", new { fencedSource });
                var acceptedAdmission = await db.QuerySingleAsync<(string TargetTenantScopeKey, string BindingHash, long Revision, string StatusKey, Guid? TargetInstanceId)>(
                    "SELECT TargetTenantScopeKey,BindingHash,Revision,StatusKey,TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@fencedCall", new { fencedCall });
                Assert.IsTrue(acceptedAdmission.StatusKey is "created" or "active");
                Assert.IsNotNull(acceptedAdmission.TargetInstanceId);
                await using (var check = factory.Services.CreateAsyncScope())
                {
                    check.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                    var commands = check.ServiceProvider.GetRequiredService<ICommandExecutor>();
                    var parameters = WorkflowSqlParameters.Create(("Id", fencedCall), ("TenantScopeKey", acceptedAdmission.TargetTenantScopeKey),
                        ("BindingHash", acceptedAdmission.BindingHash), ("Revision", acceptedAdmission.Revision), ("Now", DateTimeOffset.UtcNow));
                    Assert.AreEqual(0, await commands.ExecuteAsync(WorkflowCollaborationControlSql.CloseRejectedStart, parameters, CancellationToken.None),
                        "旧启动失败不得关闭已经创建目标的准入。");
                }
                var unchangedAdmission = await db.QuerySingleAsync<(string StatusKey, long Revision, Guid? TargetInstanceId)>(
                    "SELECT StatusKey,Revision,TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@fencedCall", new { fencedCall });
                Assert.AreEqual(acceptedAdmission.StatusKey, unchangedAdmission.StatusKey);
                Assert.AreEqual(acceptedAdmission.Revision, unchangedAdmission.Revision);
                Assert.AreEqual(acceptedAdmission.TargetInstanceId, unchangedAdmission.TargetInstanceId);
                // 在回滚事务中构造尚未创建的状态，验证正确快照可关闭，错误快照不能关闭。
                if (db.State != System.Data.ConnectionState.Open) await db.OpenAsync();
                await using (var cas = await db.BeginTransactionAsync())
                {
                    try
                    {
                        await db.ExecuteAsync("UPDATE fn_workflow_collaboration_admission SET StatusKey='requested',TargetInstanceId=NULL WHERE Id=@fencedCall", new { fencedCall }, cas);
                        var input = new { Id = fencedCall, TenantScopeKey = acceptedAdmission.TargetTenantScopeKey,
                            BindingHash = acceptedAdmission.BindingHash, Revision = acceptedAdmission.Revision, Now = DateTimeOffset.UtcNow };
                        var sql = WorkflowCollaborationControlSql.CloseRejectedStart.Text;
                        Assert.AreEqual(0, await db.ExecuteAsync(sql, input with { Revision = input.Revision + 1 }, cas));
                        Assert.AreEqual(0, await db.ExecuteAsync(sql, input with { BindingHash = new string('0', 64) }, cas));
                        Assert.AreEqual(0, await db.ExecuteAsync(sql, input with { TenantScopeKey = "host:wrong" }, cas));
                        Assert.AreEqual(1, await db.ExecuteAsync(sql, input, cas));
                    }
                    finally { await cas.RollbackAsync(); }
                }
                fencedTarget = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@fencedCall", new { fencedCall });
                var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@fencedTarget AND StatusKey='active'", new { fencedTarget });
                var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
                using var approved = await target.PostAsJsonAsync($"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(),
                    expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { title = "Kafka result" }, comment = "TTL fenced", idempotencyKey = key });
                Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());
                await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@fencedTarget", new { fencedTarget }) == "completed", "目标审批必须实际完成");
                await Task.Delay(TimeSpan.FromSeconds(12));
                await AssertFencedCompletionAsync();
            }

            async Task AssertFencedCompletionAsync()
            {
                Assert.AreNotEqual(Guid.Empty, fencedCall);
                Assert.AreEqual("accepted", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@fencedCall", new { fencedCall }));
                Assert.AreEqual("active", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@fencedSource", new { fencedSource }));
                Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@fencedTarget", new { fencedTarget }));
                Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_control WHERE CallId=@fencedCall AND KindKey='completed'", new { fencedCall }));
            }

            async Task RunAsync(bool cancel, bool pauseBroker = false, HttpClient? startClient = null)
            {
                var key = Guid.NewGuid().ToString("N");
                Task<JsonElement> StartAsync() => PostAsync(startClient ?? source, "/api/v1/workflow/instances", new { definitionVersionId = sourceAsset!.Version, businessType = "kafka.workflow", businessId = key, idempotencyKey = key,
                    initialValues = new { title = "shared", secret = "source-private" } }, HttpStatusCode.Created);
                JsonElement started = default;
                if (pauseBroker) await pipeline.InterruptBrokerAsync(TimeSpan.FromSeconds(4), observeWhilePaused: async _ =>
                {
                    started = await StartAsync();
                    var id = started.GetProperty("id").GetGuid();
                    // requested准入由源事务预留；暂停期间只能保留该栅栏，不能收件或创建目标。
                    Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_admission AS a INNER JOIN fn_workflow_collaboration_call AS c ON c.Id=a.Id WHERE c.InstanceId=@id AND a.StatusKey='requested' AND a.TargetInstanceId IS NULL", new { id }));
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_delivery AS d INNER JOIN fn_workflow_collaboration_call AS c ON c.Id=d.CallId WHERE c.InstanceId=@id", new { id }), "Broker暂停时不得通过Legacy收件");
                    Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin AS o INNER JOIN fn_workflow_collaboration_call AS c ON c.Id=o.CallId WHERE c.InstanceId=@id", new { id }), "Broker暂停时不得创建目标");
                });
                else started = await StartAsync();
                var instance = started.GetProperty("id").GetGuid();
                await WaitKafkaAsync(async () =>
                {
                    var status = await db.ExecuteScalarAsync<string?>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE InstanceId=@instance", new { instance });
                    Assert.AreNotEqual("denied", status, "协作请求已被拒绝，不能继续等待接纳。");
                    return status == "accepted";
                }, "Kafka请求和创建确认");
                var call = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_collaboration_call WHERE InstanceId=@instance", new { instance });
                var targetId = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call });
                if (cancel)
                {
                    var current = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}");
                    // 操作回执按实例共享键空间；取消必须使用独立于启动的幂等键。
                    await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = current.GetProperty("revision").GetInt64(), reason = "Kafka cancel", idempotencyKey = key + "-cancel" });
                }
                else
                {
                    var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@targetId AND StatusKey='active'", new { targetId });
                    var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}");
                    await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(), expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { title = "Kafka result" }, comment = "Kafka", idempotencyKey = key });
                }
                var state = cancel ? "cancelled" : "completed";
                await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }) == state, "Kafka终态收件");
                // 结果收件先提交耐久续办；必须由生产恢复循环走到真实实例终态。
                await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }) == state, "源实例耐久续办终态");
                await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetId", new { targetId }) == state, "目标实例终态");
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin WHERE CallId=@call", new { call }));
                var values = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
                Assert.AreEqual("source-private", values.GetProperty("secret").GetString());
                if (!cancel) Assert.AreEqual("Kafka result", values.GetProperty("answer").GetString());
            }
            }
            finally { if (apiHost is not null) await StopNativeAsync(apiHost, Path.Combine(data!, $"api-{phase}.log")); }
        }

        if (native)
        {
            Assert.AreEqual(nativeApi!.Digest, await NativeDigestAsync(Path.Combine(nativeApi.Directory, "publish", nativeApi.Executable)));
            Assert.AreEqual(nativeWorker!.Digest, await NativeDigestAsync(Path.Combine(nativeWorker.Directory, "publish", nativeWorker.Executable)));
            Console.WriteLine("Workflow Native Kafka运行证据：" + data);
        }

        Dictionary<string, string> NativeSettings(Dictionary<string, string?> settings)
        {
            var values = settings.Where(item => item.Value is not null).ToDictionary(item => item.Key.Replace(":", "__", StringComparison.Ordinal), item => item.Value!);
            values["Messaging__Kafka__BootstrapServers"] = pipeline.ContainerClientBootstrapServers;
            values["Messaging__Worker__Mode"] = "HybridKafka";
            values["Messaging__KafkaConnectRollback__ConnectBaseUri"] = new UriBuilder(controlAddress) { Host = "host.docker.internal" }.Uri.AbsoluteUri;
            values["DataProtection__KeyRingPath"] = "/data/keys"; values["Files__Local__RootPath"] = "/data/files";
            return values;
        }

        Dictionary<string, string?> Settings(int index)
        {
            var settings = new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple", ["Messaging:DeliveryCutover:Enabled"] = "true",
                ["Messaging:ShadowCapture:Enabled"] = "true", ["Messaging:ShadowCapture:EventType"] = streams[index].EventType,
                // 本例独立Broker使用明文开发监听；真实Host不能继承部署配置中的SSL协议。
                ["Messaging:Kafka:Enabled"] = "true", ["Messaging:Kafka:SecurityProtocol"] = "Plaintext", ["Messaging:Kafka:BootstrapServers"] = pipeline.BootstrapServers, ["Messaging:Kafka:ClientId"] = "fullnet.workflow.physical.worker", ["Messaging:Kafka:ConsumerInstanceId"] = "",
                ["Messaging:KafkaConnectRollback:Enabled"] = "true", ["Messaging:KafkaConnectRollback:ConnectBaseUri"] = controlAddress.AbsoluteUri,
                ["Workflow:RecoveryWorker:PollMilliseconds"] = "100", ["Workflow:RecoveryWorker:ScanIntervalSeconds"] = "1" };
            for (var i = 0; i < streams.Length; i++)
            {
                var key = "Messaging:KafkaConnectRollback:Streams:" + i + ":";
                foreach (var (name, value) in new Dictionary<string, string> { ["EventType"] = streams[i].EventType, ["SchemaVersion"] = "1", ["ConnectorName"] = formal + "-" + i, ["ShadowConnectorName"] = shadow + "-" + i,
                    ["TopicName"] = streams[i].Topic, ["ShadowTopicName"] = "fullnet.dev.shadow." + streams[i].Topic, ["SourceDatabaseName"] = sourceDatabase, ["WorkerClientId"] = "fullnet.workflow.physical.worker", ["ConsumerGroupId"] = group }) settings[key + name] = value;
            }
            return settings;
        }

        async Task<Dictionary<string, string>> ConnectorAsync(string name, bool append, string topic)
        {
            var sql = provider == DatabaseProvider.SqlServer;
            var file = $"deploy/messaging/connectors/{(sql ? "sqlserver" : "mysql")}-outbox-{(append ? "capacity" : "shadow")}.json";
            using var template = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), file)));
            var ms = sql ? new SqlConnectionStringBuilder(connection) : null;
            var my = sql ? null : new MySqlConnectionStringBuilder(connection);
            var values = new Dictionary<string, string> { ["FULLNET_SQLSERVER_HOST"] = pipeline.HostGateway, ["FULLNET_MYSQL_HOST"] = pipeline.HostGateway,
                ["FULLNET_SQLSERVER_PORT"] = ms?.DataSource.Split(',').Last() ?? "1433", ["FULLNET_MYSQL_PORT"] = my?.Port.ToString() ?? "3306",
                ["FULLNET_SQLSERVER_USER"] = ms?.UserID ?? "", ["FULLNET_SQLSERVER_PASSWORD"] = ms?.Password ?? "", ["FULLNET_SQLSERVER_DATABASE"] = sourceDatabase,
                ["FULLNET_MYSQL_USER"] = "root", ["FULLNET_MYSQL_PASSWORD"] = SharedDatabaseFixture.MySqlRootPassword, ["FULLNET_MYSQL_DATABASE"] = sourceDatabase,
                ["FULLNET_KAFKA_BOOTSTRAP_SERVERS"] = pipeline.InternalKafkaBootstrapServers, ["FULLNET_SHADOW_TOPIC"] = "unused" };
            var result = template.RootElement.GetProperty("config").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            foreach (var key in result.Keys.ToArray()) foreach (var (token, value) in values) result[key] = result[key].Replace("${" + token + "}", value, StringComparison.Ordinal);
            Assert.IsFalse(result.Values.Any(v => v.Contains("${FULLNET_", StringComparison.Ordinal)), "模板部署变量必须全部解析，不能回显凭据。");
            result = new Dictionary<string, string>(CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(name, result));
            result["transforms.outbox.route.topic.regex"] = "^fullnet\\.(.*)$";
            result["transforms.outbox.route.topic.replacement"] = (append ? "" : "fullnet.dev.shadow.") + "$1.v1";
            // 每条流拥有独立Connector与Offset；按路由后Topic过滤，回退一条流不能暂停其它流的发布者。
            var stream = streams.Single(item => item.Topic == topic);
            result["transforms"] = "fullnet_filter,outbox,select";
            result["transforms.fullnet_filter.type"] = "net.full.connect.FullNetOutboxStreamFilter";
            result["transforms.fullnet_filter.source.topic"] = result["topic.prefix"] + "." + sourceDatabase
                + (sql ? ".dbo." : ".") + (append ? "fn_messaging_outbox_event" : "fn_messaging_shadow_event");
            result["transforms.fullnet_filter.event.type"] = stream.EventType;
            result["transforms.fullnet_filter.schema.version"] = "1";
            result["predicates"] = "selected";
            result["predicates.selected.type"] = "org.apache.kafka.connect.transforms.predicates.TopicNameMatches";
            result["predicates.selected.pattern"] = "^(?:" + System.Text.RegularExpressions.Regex.Escape((append ? "" : "fullnet.dev.shadow.") + topic)
                + "|" + System.Text.RegularExpressions.Regex.Escape(result["topic.heartbeat.prefix"] + "." + result["topic.prefix"]) + ")$";
            result["transforms.select.type"] = "org.apache.kafka.connect.transforms.Filter";
            result["transforms.select.predicate"] = "selected";
            result["transforms.select.negate"] = "true";
            result["schema.history.internal.kafka.topic"] = name + ".history";
            if (positionProbe && append && topic == streams[1].Topic)
            {
                // 单条批次与透明线代理共同制造同事务首条已确认、后续请求仍在途的实际窗口。
                result["max.batch.size"] = "1";
                result["max.queue.size"] = "2";
                result["producer.override.client.id"] = "fullnet-partial-" + name;
                result["producer.override.batch.size"] = "1";
                result["producer.override.linger.ms"] = "0";
                result["producer.override.max.request.size"] = "2000";
                result["producer.override.max.in.flight.requests.per.connection"] = "1";
            }
            if (sql) { result["database.encrypt"] = "false"; result["database.trustServerCertificate"] = "true"; }
            return result;
        }
    }

    private static async Task VerifyNativeCutoverSessionLossAsync(DatabaseProvider provider, string connection, HttpClient api, string eventType)
    {
        // SQL Server 仅在锁描述中保留资源名称前32字符，随机后缀保持完整可校验。
        var resource = "fullnet.test.cutover." + Guid.NewGuid().ToString("N")[..10];
        await using DbConnection gate = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(connection);
        await using DbConnection probe = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(connection);
        await gate.OpenAsync();
        await probe.OpenAsync();
        if (provider == DatabaseProvider.SqlServer)
        {
            Assert.IsTrue(await gate.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r", new { resource }) >= 0);
            // 测试触发器仅存在于本例随机库；审计 INSERT 已执行时阻塞，让 Native 事务停在真实提交之前。
            await gate.ExecuteAsync($"""
                CREATE TRIGGER dbo.fn_messaging_test_cutover_gate ON dbo.fn_messaging_domain_audit AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE ActionKey='messaging.delivery.cutover')
                    BEGIN
                        DECLARE @r int;
                        EXEC @r=sys.sp_getapplock @Resource='{resource}', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=180000;
                        IF @r<0 THROW 51000, 'Test cutover gate timed out', 1;
                    END
                END
                """);
        }
        else
        {
            Assert.AreEqual(1, await gate.ExecuteScalarAsync<int>("SELECT GET_LOCK(@resource, 0)", new { resource }));
            await gate.ExecuteAsync($"""
                CREATE TRIGGER fn_messaging_test_cutover_gate AFTER INSERT ON fn_messaging_domain_audit FOR EACH ROW
                BEGIN
                    DECLARE acquired int;
                    IF NEW.ActionKey='messaging.delivery.cutover' THEN
                        SET acquired=GET_LOCK('{resource}', 180);
                        IF acquired<>1 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Test cutover gate timed out'; END IF;
                        DO RELEASE_LOCK('{resource}');
                    END IF;
                END
                """);
            await probe.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");
        }

        var dirtyHint = provider == DatabaseProvider.SqlServer ? " WITH (NOLOCK)" : "";
        var beforeAudit = await probe.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM fn_messaging_domain_audit{dirtyHint} WHERE ActionKey='messaging.delivery.cutover'");
        var request = new ChangeDeliveryOwnerRequest(eventType, 1, EventDeliveryOwner.CdcKafka, "Native cutover session loss probe");
        var oldRequest = api.PostAsJsonAsync("/api/v1/messaging/delivery/cutover", request);
        try
        {
            var session = 0;
            await WaitKafkaAsync(async () =>
            {
                if (oldRequest.IsCompleted)
                {
                    using var early = await oldRequest;
                    Assert.Fail($"旧切流请求在进入审计屏障前结束：{(int)early.StatusCode} {await early.Content.ReadAsStringAsync()}");
                }
                session = provider == DatabaseProvider.SqlServer
                    ? await probe.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT TOP (1) request_session_id FROM sys.dm_tran_locks WHERE resource_type='APPLICATION' AND request_owner_type='SESSION' AND resource_database_id=DB_ID() AND resource_description LIKE '%messaging.delivery_control%' AND request_status='GRANT'),0)")
                    : await probe.ExecuteScalarAsync<int>("SELECT COALESCE(IS_USED_LOCK('messaging.delivery_control'),0)");
                if (session == 0) return false;
                // 测试锁仍由独立连接持有；脏读只用于证明该持锁事务已经写到审计，不能作为业务读取。
                return await probe.ExecuteScalarAsync<int>($"SELECT CurrentOwner FROM fn_messaging_stream_ownership{dirtyHint} WHERE MessageType=@eventType AND SchemaVersion=1", new { eventType }) == 2
                    && await probe.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM fn_messaging_domain_audit{dirtyHint} WHERE ActionKey='messaging.delivery.cutover'") == beforeAudit + 1;
            }, "Native切流事务持控制锁并阻塞于审计");
            Assert.AreEqual(2, await probe.ExecuteScalarAsync<int>($"SELECT CurrentOwner FROM fn_messaging_stream_ownership{dirtyHint} WHERE MessageType=@eventType AND SchemaVersion=1", new { eventType }), "故障必须发生在所有权写入之后。");
            Assert.AreEqual(beforeAudit + 1, await probe.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM fn_messaging_domain_audit{dirtyHint} WHERE ActionKey='messaging.delivery.cutover'"), "故障必须发生在审计写入之后。");
            await probe.ExecuteAsync("KILL " + session.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using (var response = await oldRequest) Assert.IsTrue((int)response.StatusCode >= 500, "失锁请求不得报告切流成功。");
            await WaitKafkaAsync(async () => await probe.ExecuteScalarAsync<int>($"SELECT CurrentOwner FROM fn_messaging_stream_ownership{dirtyHint} WHERE MessageType=@eventType AND SchemaVersion=1", new { eventType }) == 0, "失锁事务回滚所有权");
            Assert.AreEqual(beforeAudit, await probe.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM fn_messaging_domain_audit{dirtyHint} WHERE ActionKey='messaging.delivery.cutover'"), "失锁事务不得留下成功审计。");
        }
        finally
        {
            await gate.ExecuteAsync(provider == DatabaseProvider.SqlServer
                ? "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session'" : "DO RELEASE_LOCK(@resource)", new { resource });
        }
    }

    private static async Task WaitKafkaAsync(Func<Task<bool>> predicate, string condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(180);
        while (!await predicate()) { Assert.IsTrue(DateTimeOffset.UtcNow < deadline, "物理链路超时：" + condition); await Task.Delay(250); }
    }

    /// <summary>只观察真实消费异常的类型、机器码和代码栈；不替换Handler、不输出异常正文或业务载荷。</summary>
    private sealed class KafkaDispatchExceptionProbe : IDisposable
    {
        private readonly HashSet<string> _seen = [];
        [ThreadStatic] private static bool _writing;
        internal KafkaDispatchExceptionProbe() => AppDomain.CurrentDomain.FirstChanceException += Observe;
        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= Observe;
        private void Observe(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if (_writing) return;
            var error = args.Exception; var stack = error.StackTrace;
            if (stack?.Contains("Full.NET.Messaging.Kafka.KafkaConsumerMessageProcessor", StringComparison.Ordinal) != true) return;
            var code = System.Text.RegularExpressions.Regex.IsMatch(error.Message, @"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$") ? error.Message : "redacted";
            var databaseCode = error is MySqlException mysql ? mysql.ErrorCode.ToString() : error is SqlException sql ? sql.Number.ToString() : "none";
            var frames = string.Join(" | ", stack.Split('\n').Where(line => line.Contains("Full.NET.", StringComparison.Ordinal)).Take(4));
            var summary = error.GetType().Name + " code=" + code + " database=" + databaseCode + " stack=" + frames;
            lock (_seen) { if (_seen.Count >= 16 || !_seen.Add(summary)) return; }
            try { _writing = true; Console.WriteLine("Workflow Kafka dispatch exception: " + summary); }
            finally { _writing = false; }
        }
    }
}
