using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>两个真实 API 与 Worker 进程使用同一隔离业务库；故障通过测试代理与进程终止产生。</summary>
[TestClass]
[DoNotParallelize]
public sealed partial class MessagingMultiProcessRecoveryTests
{
    private const string EventType = "fullnet.organization.unit.changed";
    private const string Group = "fullnet.identity.organization-unit-projection";
    private const string Shadow = "fullnet.dev.shadow.organization.unit-changed.v1";
    private static string Topic => IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged.TopicCode;
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MySql_two_api_two_worker_processes_recover_durable_operations()
        => await RunMySqlAsync(soak: false);

    [TestMethod]
    public async Task MySql_two_real_workers_bounded_soak()
        => await RunMySqlAsync(soak: true);

    private async Task RunMySqlAsync(bool soak)
    {
        // 本场景需要真实 Binlog 和故障触发器，只使用独享且有硬限额的实例，不接管共享数据库。
        await using var server = new MySqlBuilder("mysql:8.0")
            .WithDatabase("fullnet_process_recovery").WithUsername("root").WithPassword(SharedDatabaseFixture.MySqlRootPassword)
            .WithCommand("--log-bin-trust-function-creators=1", "--log-bin=mysql-bin", "--binlog-format=ROW", "--binlog-row-image=FULL", "--server-id=1840172610")
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new(); limits.Memory = 1536L << 20;
                limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        await VerifyAsync(new() { Provider = DatabaseProvider.MySql, ConnectionString = server.GetConnectionString(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 }, soak, server.Id);
    }

    [TestMethod]
    public async Task SqlServer_two_api_two_worker_processes_recover_durable_operations()
        => await RunSqlServerAsync(soak: false);

    [TestMethod]
    public async Task SqlServer_two_real_workers_bounded_soak()
        => await RunSqlServerAsync(soak: true);

    private async Task RunSqlServerAsync(bool soak)
    {
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new(); limits.Memory = 3L << 30;
                limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var connection = new SqlConnectionStringBuilder(server.GetConnectionString());
        await using (var admin = new SqlConnection(connection.ConnectionString)) await admin.ExecuteAsync("CREATE DATABASE fullnet_process_recovery");
        connection.InitialCatalog = "fullnet_process_recovery";
        await VerifyAsync(new() { Provider = DatabaseProvider.SqlServer, ConnectionString = connection.ConnectionString }, soak, server.Id);
    }

    private async Task VerifyAsync(DatabaseOptions database, bool soak, string databaseId)
    {
        var automaticTtlTarget = AutomaticTtlTarget(soak);
        if ((soak || automaticTtlTarget is not null)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FULLNET_KAFKA_STREAM_FILTER_JAR")))
            throw new InvalidOperationException("Native 长稳和 TTL 必须加载经摘要校验的真实流过滤插件。");
        Step(database, "bootstrap isolated database");
        await using var bootstrap = new FullNetApiFactory(database.Provider, database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" });
        await bootstrap.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection connection = database.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(database.ConnectionString) : new MySqlConnection(MySqlConnectionStringPolicy.Create(database.ConnectionString, database.MySqlGuidStorageMode, false));
        await connection.OpenAsync();
        if (database.Provider == DatabaseProvider.SqlServer)
        {
            var enabled = await SqlServerCdcTestSupport.TryEnableCdcAsync(database.ConnectionString);
            Assert.IsTrue(enabled.Succeeded, enabled.FailureReason);
            await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "deploy/messaging/sqlserver/enable-shadow-cdc.sql")));
        }
        await using var wire = new MessagingKafkaFaultProxy(MessagingTestProcess.NativeEnabled);
        await using var pipeline = await CdcDebeziumPipelineEnvironment.StartAsync(wire.Port, MessagingTestProcess.NativeEnabled, automaticTtl: automaticTtlTarget is not null);
        wire.UpstreamPort = pipeline.KafkaMappedPort;
        using var realConnect = pipeline.CreateConnectAdminClient();
        await using var control = await MessagingHttpFaultProxy.StartAsync(pipeline.ConnectBaseUri, MessagingTestProcess.NativeEnabled);
        var name = "fullnet-process-" + Guid.NewGuid().ToString("N");
        var shadowName = name + "-shadow";
        var source = database.Provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog : new MySqlConnectionStringBuilder(database.ConnectionString).Database;
        var settings = MessagingCutoverRollbackDebeziumTests.Settings(pipeline, source, name, shadowName, "fullnet.multiprocess.worker");
        if (MessagingTestProcess.NativeEnabled) settings["Messaging:Kafka:BootstrapServers"] = pipeline.ContainerClientBootstrapServers;
        var sqlEndpoint = database.Provider == DatabaseProvider.SqlServer ? new SqlConnectionStringBuilder(database.ConnectionString) : null;
        var mysqlEndpoint = database.Provider == DatabaseProvider.MySql ? new MySqlConnectionStringBuilder(database.ConnectionString) : null;
        var databasePort = sqlEndpoint is not null
            ? int.Parse(sqlEndpoint.DataSource[(sqlEndpoint.DataSource.LastIndexOf(',') + 1)..], System.Globalization.CultureInfo.InvariantCulture)
            : checked((int)mysqlEndpoint!.Port);
        await using var databaseWire = new MessagingDatabaseFaultProxy(databasePort, MessagingTestProcess.NativeEnabled);
        await using var connectorDatabaseWire = new MessagingDatabaseFaultProxy(databasePort, dockerReachable: true);
        if (sqlEndpoint is not null)
        {
            sqlEndpoint.DataSource = $"127.0.0.1,{databaseWire.Port}"; sqlEndpoint.ConnectTimeout = 3; sqlEndpoint.ConnectRetryCount = 0;
            settings["Database:ConnectionString"] = sqlEndpoint.ConnectionString;
        }
        else
        {
            mysqlEndpoint!.Server = "127.0.0.1"; mysqlEndpoint.Port = checked((uint)databaseWire.Port); mysqlEndpoint.ConnectionTimeout = 3;
            settings["Database:ConnectionString"] = mysqlEndpoint.ConnectionString;
        }
        settings["Messaging:KafkaConnectRollback:ConnectBaseUri"] = control.Address.ToString();
        settings["Messaging:Kafka:SecurityProtocol"] = "Plaintext";
        settings["Messaging:KafkaReplay:Enabled"] = "true";
        settings["Messaging:KafkaReplay:ExecutionTimeoutSeconds"] = "30";
        settings["Messaging:Kafka:DeadLetterProjectionGroupId"] = "fullnet.multiprocess.dlq";
        settings["Messaging:Kafka:RetryStages:0"] = "1s";
        settings["Messaging:Kafka:RetryStages:1"] = "2s";
        settings["Messaging:Kafka:RetryStages:2"] = "3s";
        settings["Messaging:Kafka:PartitionBufferHighWatermark"] = CapacityPartitionBuffer().ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings["Messaging:Kafka:PartitionBufferLowWatermark"] = "0";
        settings["Messaging:Kafka:PartitionKeyConcurrencySlots"] = "1";
        settings["Messaging:Kafka:OffsetCommitMode"] = "PerMessage";
        var kafka = CdcDebeziumE2ESupport.CreateKafkaOptions(pipeline, "fullnet.multiprocess.observer");
        kafka.RetryStages = ["1s", "2s", "3s"];
        using var broker = new AdminClientBuilder(kafka.BuildClientConfig()).Build();
        foreach (var topic in new[] { Topic, Shadow, KafkaTopicNames.GetDeadLetterTopic(Topic) }.Concat(kafka.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(Topic, stage))))
            await broker.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1,
                Configs = automaticTtlTarget is not null && topic == (automaticTtlTarget == "formal" ? Topic : KafkaTopicNames.GetRetryTopic(Topic, kafka.RetryStages[0]))
                    ? new Dictionary<string, string> { ["cleanup.policy"] = "delete", ["retention.ms"] = "-1", ["retention.bytes"] = "-1",
                        ["segment.ms"] = "1000", ["segment.jitter.ms"] = "0", ["message.timestamp.type"] = "LogAppendTime" } : [] }]);
        await RegisterAsync(name, true); await RegisterAsync(shadowName, false);
        await using var apiB = await MessagingTestProcess.StartAsync("Api", "b", database, settings);
        await using var apiA = await MessagingTestProcess.StartAsync("Api", "a", database, settings);
        await using var workerB = await WorkerAsync("b");
        await using var workerA = await WorkerAsync("a");
        Assert.AreEqual(4, new[] { apiA.Id, apiB.Id, workerA.Id, workerB.Id }.Distinct().Count());
        using var clientA = apiA.Client(); using var clientB = apiB.Client(); using var business = apiB.Client();
        clientA.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(clientA, default));
        clientB.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(clientB, default));
        var hostToken = await FullNetApiFactoryLogin.LoginAsHostAdminAsync(business, default);
        var tenant = await FullNetApiFactoryLogin.EnterAcmeTenantAsync(business, hostToken, default);
        business.DefaultRequestHeaders.Authorization = new("Bearer", tenant.AccessToken);
        var units = new List<OrganizationUnitResponse>();
        Step(database, "two real Worker members and genuine Legacy shadow capture");
        var requiredAssignments = new[] { Topic }.Concat(kafka.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(Topic, stage)))
            .Select(topic => new TopicPartition(topic, 0)).ToHashSet();
        // 成员数先于 rebalance 完成可见；必须等正式和重试的完整分配稳定，才能申请真实切流证明。
        await WaitAsync(async () =>
        {
            var group = (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single();
            var assigned = group.Members.SelectMany(member => member.Assignment.TopicPartitions).ToArray();
            return group.State == ConsumerGroupState.Stable && group.Members.Count == 2
                && requiredAssignments.SetEquals(assigned) && assigned.Distinct().Count() == assigned.Length;
        }, "两个真实 Worker 的正式/重试分配稳定");
        await PostAsync<ShadowCaptureResponse>(clientB, "/api/v1/messaging/delivery/shadow-captures/",
            new StartShadowCaptureRequest(Guid.CreateVersion7(), null, EventType, 1, 1, 300, "process recovery shadow"));
        await CreateAsync("legacy");
        await WaitAsync(async () => await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_shadow_capture WHERE State='closed'") == 1, "影子采集关闭");
        var sampleId = await connection.QuerySingleAsync<Guid>("SELECT Id FROM fn_messaging_shadow_event WHERE MessageType=@EventType", new { EventType });
        Assert.IsNotNull(await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(pipeline, Shadow, sampleId, TimeSpan.FromSeconds(120)));
        try
        {
            await PostAsync<CutoverEvidenceResponse>(clientB, "/api/v1/messaging/delivery/cutover-evidence", new VerifyCutoverRequest(EventType, 1, "actual process members"));
        }
        catch
        {
            try
            {
                var group = (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single();
                var readiness = new { Provider = database.Provider.ToString(), Formal = await ReadConnectorStatesAsync(realConnect, name),
                    Shadow = await ReadConnectorStatesAsync(realConnect, shadowName), State = group.State.ToString(),
                    Members = group.Members.Select(member => new { member.ClientId, Assigned = member.Assignment.TopicPartitions.Select(partition => partition.ToString()).ToArray() }).ToArray() };
                await File.AppendAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, "cutover-readiness-failure.ndjson"),
                    System.Text.Json.JsonSerializer.Serialize(readiness) + Environment.NewLine);
            }
            catch (Exception diagnostic) { TestContext.WriteLine("切流诊断失败：" + diagnostic.GetType().Name); }
            throw;
        }
        await PostAsync<DeliveryCutoverResponse>(clientB, "/api/v1/messaging/delivery/cutover", Change(EventDeliveryOwner.CdcKafka));
        var topologyJson = await connection.QuerySingleAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@EventType AND SchemaVersion=1", new { EventType });
        Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(topologyJson, out var topology));
        Assert.AreEqual(Topic, topology!.TopicName);
        Assert.AreEqual(name, topology.ConnectorName);
        Assert.AreEqual(4, topology.Topics.Length, "正式 Topic 和实际配置的三个重试阶段必须全部持久保存。");
        if (MessagingTestProcess.NativeEnabled)
        {
            var observedCluster = await broker.DescribeClusterAsync(new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
            Assert.AreEqual(topology.KafkaClusterId, observedCluster.ClusterId, "观察端与 Native 控制端必须访问同一实际 Broker。");
            await File.AppendAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, "native-routing.ndjson"),
                System.Text.Json.JsonSerializer.Serialize(new { Provider = database.Provider.ToString(), ObserverBootstrap = pipeline.BootstrapServers,
                    NativeBootstrap = pipeline.ContainerClientBootstrapServers, ProxyUpstreamPort = pipeline.KafkaMappedPort,
                    ClusterId = observedCluster.ClusterId, NativeBindingClusterId = topology.KafkaClusterId }) + Environment.NewLine);
        }
        var migrations = typeof(Full.NET.Migrations.DbUp.DbUpMigrationRunner).Assembly;
        var providerSegment = database.Provider == DatabaseProvider.SqlServer ? ".SqlServer." : ".MySql.";
        var resourceName = migrations.GetManifestResourceNames().Single(resource => resource.EndsWith(providerSegment + "342_MessagingDeliveryTopology.sql", StringComparison.Ordinal));
        using var scriptReader = new StreamReader(migrations.GetManifestResourceStream(resourceName)!);
        var ddl = await scriptReader.ReadToEndAsync();
        // 模拟可空列已创建但 DbUp 尚未记账；重复执行不得重建或覆盖已冻结的基线。
        if (database.Provider == DatabaseProvider.MySql)
        {
            // DELIMITER 是客户端指令；驱动整批发送静态过程体，不把它发给服务器解析。
            ddl = ddl.Replace("DELIMITER $$", "", StringComparison.Ordinal).Replace("DELIMITER ;", "", StringComparison.Ordinal).Replace("END$$", "END;", StringComparison.Ordinal);
            await connection.ExecuteAsync(ddl); await connection.ExecuteAsync(ddl);
        }
        else { await connection.ExecuteAsync(ddl); await connection.ExecuteAsync(ddl); }
        Assert.AreEqual(topologyJson, await connection.QuerySingleAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@EventType AND SchemaVersion=1", new { EventType }));
        await CreateAsync("cdc");
        await using var independentProbe = soak || automaticTtlTarget is not null
            ? await MessagingIndependentStreamProbe.StartAsync(database, source, connection, pipeline, realConnect, broker, kafka,
                connectorDatabaseWire.Port, EventType)
            : null;
        if (independentProbe is not null) await independentProbe.VerifyAsync("before");

        if (automaticTtlTarget is not null)
        {
            if (automaticTtlTarget == "formal") await CreateAsync("ttl-replay-next");
            await AutomaticTtlAsync(automaticTtlTarget, database, databaseId, connection, pipeline, broker, kafka, realConnect,
                control, name, clientB, business, topology!, topologyJson, LatestAsync, FenceAsync, independentProbe!);
            await workerA.StopGracefullyAsync(); await workerB.StopGracefullyAsync();
            await realConnect.DeleteConnectorAsync(name); await realConnect.DeleteConnectorAsync(shadowName);
            return;
        }

        if (soak)
        {
            await SoakAsync(database, connection, pipeline, broker, business, tenant.AccessToken, tenant.TenantId,
                workerA, workerB, apiA, apiB, WorkerAsync, databaseId, kafka, topology!, independentProbe!);
            await realConnect.DeleteConnectorAsync(name); await realConnect.DeleteConnectorAsync(shadowName);
            return;
        }

        Step(database, "alive old executor loses only its database lock; pause arrives after Recovered");
        var latePause = control.Arm("PUT", $"/connectors/{name}/pause", afterExecution: false);
        latePause.ContinueAfterClientDisconnect = true;
        var staleRollback = PostControlAsync(clientA, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        await AwaitGateAsync(latePause, staleRollback);
        var staleGeneration = (await LatestAsync()).Generation;
        await KillControlSessionAsync(connection, database.Provider);
        await RecoverAsync(staleGeneration);
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovered, (await LatestAsync()).Phase);
        var correctionCount = await CorrectionsAsync();
        latePause.Release.TrySetResult(); await latePause.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using (var staleResponse = await staleRollback) Assert.IsTrue((int)staleResponse.StatusCode >= 500, "失锁执行者必须停止完成与补偿。");
        await WaitAsync(async () => await CorrectionsAsync() > correctionCount
            && await realConnect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(1)), "Worker 实际纠正迟到 pause");
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovered, (await LatestAsync()).Phase);
        Assert.AreEqual(0, await FenceAsync());
        using (var alive = await clientA.GetAsync("/health/live")) Assert.AreEqual(HttpStatusCode.OK, alive.StatusCode);
        await CreateAsync("late-pause-corrected");

        // 暂停意图已提交、实际 REST 尚未执行，强杀原 API 后由另一进程恢复同一代次。
        Step(database, "crash API after durable pause intent, before actual pause");
        var beforePause = control.Arm("PUT", $"/connectors/{name}/pause", afterExecution: false);
        var rollback = PostControlAsync(clientA, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        await AwaitGateAsync(beforePause, rollback);
        var prepared = await LatestAsync(); Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, prepared.Phase);
        Assert.AreEqual(1, await FenceAsync());
        await apiA.CrashAsync(); beforePause.Release.TrySetResult(); await IgnoreLostResponseAsync(rollback);
        await RecoverAsync(prepared.Generation);
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovered, (await LatestAsync()).Phase);
        Assert.AreEqual(0, await FenceAsync());

        await using var apiRestart = await MessagingTestProcess.StartAsync("Api", "a-restart", database, settings);
        using var restarted = apiRestart.Client(); restarted.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(restarted, default));
        Step(database, "crash API after actual Connector pause and race recovery requests");
        var afterPause = control.Arm("PUT", $"/connectors/{name}/pause", afterExecution: true);
        rollback = PostControlAsync(restarted, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        await AwaitGateAsync(afterPause, rollback);
        await WaitAsync(() => realConnect.IsConnectorPausedAsync(name), "实际 Connector 已暂停");
        prepared = await LatestAsync(); Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, prepared.Phase);
        var resumesBeforeOldRecovery = control.ResumeCount;
        // 旧终态代次迟到不能影响当前正在准备的新代次或解除新 fence。
        var previousGeneration = await connection.QuerySingleAsync<Guid>(database.Provider == DatabaseProvider.SqlServer
            ? "SELECT TOP 1 Id FROM fn_messaging_rollback_operation WHERE Phase=4 ORDER BY CreatedAtUtc DESC"
            : "SELECT Id FROM fn_messaging_rollback_operation WHERE Phase=4 ORDER BY CreatedAtUtc DESC LIMIT 1");
        using (var lateRecovery = await clientB.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{previousGeneration}/recover", new RecoverRollbackRequest("late previous generation")))
            Assert.AreEqual(HttpStatusCode.Conflict, lateRecovery.StatusCode, "当前执行者持有真实会话锁，迟到请求须被拒绝。");
        Assert.AreEqual(resumesBeforeOldRecovery, control.ResumeCount);
        Assert.AreEqual(1, await FenceAsync());
        using (var rejected = await business.PostAsJsonAsync("/api/v1/organization/units", new CreateOrganizationUnitRequest(null, "process-fenced", "fenced", 10)))
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_organization_unit WHERE Code='process-fenced'"));
        await apiRestart.CrashAsync(); afterPause.DropResponse = true; afterPause.Release.TrySetResult(); await IgnoreLostResponseAsync(rollback);
        Step(database, "real API to Connect interruption preserves the durable fence");
        control.Disconnect = true;
        try
        {
            using var unavailable = await clientB.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{prepared.Generation}/recover", new RecoverRollbackRequest("network outage must keep protection"));
            Assert.IsTrue((int)unavailable.StatusCode >= 500, await unavailable.Content.ReadAsStringAsync());
            Assert.AreEqual(1, await FenceAsync());
            Assert.IsTrue(await realConnect.IsConnectorPausedAsync(name));
        }
        finally { control.Disconnect = false; }
        Step(database, "same-name Connector configuration drift must not resume another route");
        var originalConnector = new Dictionary<string, string>((await realConnect.TryReadConnectorConfigurationAsync(name))!)
        {
            // 恢复本用例已知凭据，不依赖管理端是否对读取结果做密码脱敏。
            ["database.password"] = SharedDatabaseFixture.MySqlRootPassword,
        };
        var changedConnector = new Dictionary<string, string>(originalConnector)
        {
            ["transforms.outbox.route.topic.replacement"] = Shadow,
        };
        await realConnect.RegisterConnectorAsync(name, changedConnector);
        try
        {
            var resumesBeforeConfigurationCheck = control.ResumeCount;
            using var changed = await clientB.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{prepared.Generation}/recover", new RecoverRollbackRequest("changed resource must retain protection"));
            Assert.AreEqual(HttpStatusCode.InternalServerError, changed.StatusCode, await changed.Content.ReadAsStringAsync());
            Assert.AreEqual(resumesBeforeConfigurationCheck, control.ResumeCount, "不同配置的同名 Connector 不得收到恢复请求。");
            Assert.AreEqual(1, await FenceAsync());
        }
        finally { await realConnect.RegisterConnectorAsync(name, originalConnector); }
        Step(database, "successful resume REST cannot clear fence while actual Connector tasks fail");
        await WaitAsync(() => realConnect.IsConnectorPausedAsync(name), "配置恢复后原 Connector 保持暂停");
        connectorDatabaseWire.SetDisconnected(true);
        try
        {
            // 单纯断网时 Connect 可能暂时仍显示 RUNNING；先使真实任务重新连接并确认 FAILED，再验收目标恢复分支。
            await realConnect.ResumeConnectorAsync(name);
            await RestartTasksAsync(pipeline.ConnectBaseUri, name, onlyFailed: false);
            await WaitAsync(async () => HasFailedTask(await realConnect.TryGetConnectorStatusAsync(name)), "源库网络中断后的实际任务 FAILED");
            Assert.AreEqual(1, await FenceAsync());
            using var failedTask = await clientB.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{prepared.Generation}/recover", new RecoverRollbackRequest("task startup must be confirmed"));
            Assert.AreEqual(HttpStatusCode.InternalServerError, failedTask.StatusCode, await failedTask.Content.ReadAsStringAsync());
            Assert.AreEqual(1, await FenceAsync(), "REST resume 成功但任务未恢复，必须保留保护。");
            var status = await realConnect.TryGetConnectorStatusAsync(name);
            Assert.IsTrue(HasFailedTask(status), "必须观察实际任务失败，不能用代理状态替代。");
        }
        finally { connectorDatabaseWire.SetDisconnected(false); }
        await RestartTasksAsync(pipeline.ConnectBaseUri, name, onlyFailed: true);
        await WaitAsync(async () => KafkaCdcMonitoringWorker.ReadConnectorRunning(await realConnect.TryGetConnectorStatusAsync(name)) == true,
            "真实失败任务已在恢复后的数据库连接上启动");
        var recovery = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => clientB.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{prepared.Generation}/recover", new RecoverRollbackRequest("concurrent takeover"))));
        foreach (var response in recovery) { using (response) Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync()); }
        await RecoverAsync(prepared.Generation); Assert.AreEqual(0, await FenceAsync());
        Assert.IsTrue(await realConnect.WaitForConnectorHealthyAsync(name, TimeSpan.FromSeconds(120)));
        await CreateAsync("after-recovery");

        Step(database, "real Worker Kafka network interruption and backlog recovery");
        wire.SetDisconnected(true);
        OrganizationUnitResponse disconnectedUnit;
        try
        {
            disconnectedUnit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken,
                "process-network-" + Guid.NewGuid().ToString("N"), "broker-disconnected", default);
            units.Add(disconnectedUnit);
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.IsFalse(await ProjectionAsync(disconnectedUnit), "隔离期间不得凭旧数据宣称新消息已完成。");
        }
        finally { wire.SetDisconnected(false); }
        await WaitAsync(() => ProjectionAsync(disconnectedUnit), "Kafka 网络恢复后真实业务排空");

        Step(database, "real API and Worker database sockets disconnect and recover");
        var operationBeforeDatabaseOutage = await LatestAsync();
        databaseWire.SetDisconnected(true);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var unavailable = await clientB.GetAsync($"/api/v1/messaging/delivery/rollback-operations?eventType={EventType}&schemaVersion=1", timeout.Token);
            // JWT 会话的权威数据库不可读时，现有认证边界返回 401；不能把合法的失败关闭误当成控制面缓存成功。
            Assert.AreEqual(HttpStatusCode.Unauthorized, unavailable.StatusCode,
                "会话无法验证时不得执行受保护操作：" + await unavailable.Content.ReadAsStringAsync());
            Assert.AreEqual((int)operationBeforeDatabaseOutage.Phase,
                await connection.ExecuteScalarAsync<int>("SELECT Phase FROM fn_messaging_rollback_operation WHERE Id=@Id", new { Id = operationBeforeDatabaseOutage.Generation }));
            Assert.AreEqual(0, await FenceAsync());
        }
        finally { databaseWire.SetDisconnected(false); }
        await WaitAsync(async () =>
        {
            using var ready = await clientB.GetAsync($"/api/v1/messaging/delivery/rollback-operations?eventType={EventType}&schemaVersion=1");
            return ready.StatusCode == HttpStatusCode.OK;
        }, "数据库网络和连接池恢复后的真实控制面读取");
        await CreateAsync("database-recovered");
        Assert.AreEqual(0, await FenceAsync(), "网络恢复不改变已完成持久操作的保护状态。");

        // Inbox 和业务已经提交但正式 Offset 尚未送 Broker，终止提交者，另一进程至少一次接管。
        Step(database, "crash Worker between Inbox commit and Broker OffsetCommit");
        await workerB.CrashAsync();
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 1, "故障点前唯一处理者");
        await WaitAsync(async () => await GroupOffsetAsync() == await BrokerEndAsync(), "故障点前原有消息位点已提交");
        var commit = wire.ArmOffsetCommit(Group);
        var pendingUnit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken, "process-crash-" + Guid.NewGuid().ToString("N"), "worker-crash", default);
        units.Add(pendingUnit);
        await commit.Reached.Task.WaitAsync(TimeSpan.FromMinutes(3));
        Assert.IsTrue(await ProjectionAsync(pendingUnit));
        await using var workerTakeover = await WorkerAsync("b-takeover");
        await workerA.CrashAsync(); commit.Drop = true; commit.Release.TrySetResult();
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 1, "Worker 崩溃后的重平衡");
        await WaitAsync(async () => (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@Group AND Status='processed'", new { Group })) >= 3, "Inbox 业务对账");
        await using var workerRestart = await WorkerAsync("a-restart");
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 2, "Worker 重启并重新入组");

        Step(database, "replay process exits after Inbox commit before checkpoint confirmation");
        await workerTakeover.CrashAsync(); await workerRestart.CrashAsync();
        var replayOffset = await BrokerEndAsync();
        var replayUnit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken,
            "process-replay-" + Guid.NewGuid().ToString("N"), "replay-crash", default);
        units.Add(replayUnit);
        await WaitAsync(async () => await BrokerEndAsync() == replayOffset + 1, "真实 CDC 事件到达固定重放窗口");
        var messageId = await connection.QuerySingleAsync<Guid>(database.Provider == DatabaseProvider.SqlServer
            ? "SELECT TOP 1 Id FROM fn_messaging_outbox_event WHERE MessageType=@EventType ORDER BY OccurredAtUtc DESC, Id DESC"
            : "SELECT Id FROM fn_messaging_outbox_event WHERE MessageType=@EventType ORDER BY OccurredAtUtc DESC, Id DESC LIMIT 1", new { EventType });
        await using var replayApi = await MessagingTestProcess.StartAsync("Api", "replay-crash", database, settings);
        using var replayClient = replayApi.Client();
        replayClient.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(replayClient, default));
        using var createdReplay = await clientB.PostAsJsonAsync("/api/v1/messaging/kafka/replay-operations/",
            new CreateKafkaReplayOperationRequest(Guid.CreateVersion7(), Topic, null, null,
                replayOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), replayOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), [0], Group, 1, "fixed genuine business window"));
        Assert.AreEqual(HttpStatusCode.Created, createdReplay.StatusCode, await createdReplay.Content.ReadAsStringAsync());
        var replayOperation = (await createdReplay.Content.ReadFromJsonAsync<KafkaReplayOperationResponse>())!;
        var groupBeforeReplay = await GroupOffsetAsync();
        await using (var checkpoint = await MessagingReplayCheckpointGate.CreateAsync(connection, database.Provider))
        {
            var execution = replayClient.PostAsJsonAsync($"/api/v1/messaging/kafka/replay-operations/{replayOperation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("commit Inbox before checkpoint"));
            await WaitAsync(async () => await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@Group AND MessageId=@MessageId AND Status='processed'", new { Group, MessageId = messageId }) == 1, "重放 Inbox 真实提交");
            Assert.IsTrue(await ProjectionAsync(replayUnit));
            await replayApi.CrashAsync(); await checkpoint.ReleaseAsync(); await IgnoreLostResponseAsync(execution);
        }
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_replay_operation WHERE Id=@Id AND InFlightOffset IS NOT NULL", new { Id = replayOperation.OperationId }));
        // 尊重持久租约，不手工修改时间、代次或完成状态来加速接管。
        await WaitAsync(async () => await connection.ExecuteScalarAsync<int>(database.Provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(*) FROM fn_messaging_replay_operation WHERE Id=@Id AND LeaseUntilUtc<=SYSDATETIMEOFFSET()"
            : "SELECT COUNT(*) FROM fn_messaging_replay_operation WHERE Id=@Id AND LeaseUntilUtc<=UTC_TIMESTAMP(6)", new { Id = replayOperation.OperationId }) == 1, "原执行租约自然到期");
        var resumedReplay = await PostAsync<KafkaReplayOperationResponse>(clientB,
            $"/api/v1/messaging/kafka/replay-operations/{replayOperation.OperationId}/execute", new ChangeKafkaReplayOperationRequest("resume same operation through Inbox"));
        Assert.AreEqual(replayOperation.OperationId, resumedReplay.OperationId);
        Assert.AreEqual("completed", resumedReplay.State);
        Assert.AreEqual(groupBeforeReplay, await GroupOffsetAsync(), "范围重放不能修改正式消费组提交位点。");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@Group AND MessageId=@MessageId AND Status='processed'", new { Group, MessageId = messageId }));
        await using var workerAfterReplayA = await WorkerAsync("after-replay-a");
        await using var workerAfterReplayB = await WorkerAsync("after-replay-b");
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 2, "恢复正式 Worker 双实例");

        Step(database, "DLQ projection commits then process exits before OffsetCommit");
        await workerAfterReplayB.CrashAsync();
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 1, "DLQ 故障前唯一 Worker");
        // 故障触发器只属于本用例独享库；真实业务事件仍由 HTTP/CDC 和正式重试链产生。
        await connection.ExecuteAsync(database.Provider == DatabaseProvider.SqlServer
            ? "CREATE TRIGGER dbo.fn_messaging_test_projection_failure ON dbo.fn_identity_organization_unit_projection AFTER INSERT, UPDATE AS THROW 51001, 'Isolated test projection failure', 1"
            : "CREATE TRIGGER fn_messaging_test_projection_failure BEFORE INSERT ON fn_identity_organization_unit_projection FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Isolated test projection failure'");
        var dlqCommit = wire.ArmOffsetCommit("fullnet.multiprocess.dlq");
        var dlqUnit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken,
            "process-dlq-" + Guid.NewGuid().ToString("N"), "dlq-crash", default);
        units.Add(dlqUnit);
        await dlqCommit.Reached.Task.WaitAsync(TimeSpan.FromMinutes(3));
        var dlqId = await connection.QuerySingleAsync<Guid>("SELECT Id FROM fn_messaging_dead_letter WHERE ConsumerName=@Group AND ReplayedAtUtc IS NULL", new { Group });
        await connection.ExecuteAsync(database.Provider == DatabaseProvider.SqlServer
            ? "DROP TRIGGER dbo.fn_messaging_test_projection_failure" : "DROP TRIGGER fn_messaging_test_projection_failure");
        using (var replayDlq = await clientB.PostAsJsonAsync($"/api/v1/messaging/kafka/dead-letters/{dlqId}/replay", new { reason = "remove isolated fault and replay original envelope" }))
            Assert.AreEqual(HttpStatusCode.OK, replayDlq.StatusCode, await replayDlq.Content.ReadAsStringAsync());
        await WaitAsync(() => ProjectionAsync(dlqUnit), "真实 DLQ 原信封重放业务投影");
        var recoveredAt = await connection.ExecuteScalarAsync<object>("SELECT ReplayedAtUtc FROM fn_messaging_dead_letter WHERE Id=@Id", new { Id = dlqId });
        Assert.IsNotNull(recoveredAt);
        await workerAfterReplayA.CrashAsync(); dlqCommit.Drop = true; dlqCommit.Release.TrySetResult();
        await using var dlqTakeoverA = await WorkerAsync("dlq-takeover-a");
        await using var dlqTakeoverB = await WorkerAsync("dlq-takeover-b");
        await WaitAsync(async () =>
        {
            var offset = (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions("fullnet.multiprocess.dlq", [new TopicPartition(KafkaTopicNames.GetDeadLetterTopic(Topic), 0)])])).Single().Partitions.Single().Offset.Value;
            return offset > 0;
        }, "另一 Worker 重读并提交 DLQ 位点");
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_dead_letter WHERE Id=@Id", new { Id = dlqId }));
        Assert.AreEqual(recoveredAt, await connection.ExecuteScalarAsync<object>("SELECT ReplayedAtUtc FROM fn_messaging_dead_letter WHERE Id=@Id", new { Id = dlqId }), "重复投影不能覆盖已恢复状态。");
        await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 2, "DLQ 接管后双 Worker 稳定");

        Step(database, "final rollback commit with lost HTTP response");
        await using var finalApi = await MessagingTestProcess.StartAsync("Api", "final-response-loss", database, settings);
        using var finalClient = finalApi.Client();
        finalClient.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(finalClient, default));
        Step(database, "alive old recovery loses its lock; resume remains in flight until Completed");
        var pendingPause = control.Arm("PUT", $"/connectors/{name}/pause", afterExecution: false);
        pendingPause.ContinueAfterClientDisconnect = true;
        var stalePreparation = PostControlAsync(finalClient, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        await AwaitGateAsync(pendingPause, stalePreparation);
        var recoveringGeneration = (await LatestAsync()).Generation;
        await KillControlSessionAsync(connection, database.Provider);
        var lateResume = control.Arm("PUT", $"/connectors/{name}/resume", afterExecution: false);
        lateResume.ContinueAfterClientDisconnect = true;
        var staleRecovery = PostControlAsync(clientB, $"/api/v1/messaging/delivery/rollback-operations/{recoveringGeneration}/recover", new RecoverRollbackRequest("alive stale executor"));
        await AwaitGateAsync(lateResume, staleRecovery);
        await KillControlSessionAsync(connection, database.Provider);
        pendingPause.Release.TrySetResult(); await pendingPause.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using (var staleResponse = await stalePreparation) Assert.IsTrue((int)staleResponse.StatusCode >= 500);
        await PostAsync<RollbackOperationResponse>(finalClient, $"/api/v1/messaging/delivery/rollback-operations/{recoveringGeneration}/recover", new RecoverRollbackRequest("new session recovers same generation"));
        await using var responseProxy = await MessagingHttpFaultProxy.StartAsync(finalApi.Address);
        using var throughProxy = new HttpClient { BaseAddress = responseProxy.Address, Timeout = TimeSpan.FromMinutes(8) };
        throughProxy.DefaultRequestHeaders.Host = "localhost"; throughProxy.DefaultRequestHeaders.Authorization = finalClient.DefaultRequestHeaders.Authorization;
        var lost = responseProxy.Arm("POST", "/api/v1/messaging/delivery/rollback", afterExecution: true);
        var final = PostControlAsync(throughProxy, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        await AwaitGateAsync(lost, final);
        Assert.AreEqual(HttpStatusCode.OK, lost.StatusCode);
        var completed = await LatestAsync(); Assert.AreEqual(EventDeliveryRollbackPhase.Completed, completed.Phase);
        // 当前存活的接管 Worker 日志必须记录一次真实纠偏；不能将尚未生效的 202 当作物理终态。
        var terminalCorrectionCount = await CorrectionsAsync();
        lateResume.Release.TrySetResult(); await lateResume.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using (var staleResponse = await staleRecovery) Assert.IsTrue((int)staleResponse.StatusCode >= 500, "迟到恢复不得清除后续代次。");
        await WaitAsync(async () => await CorrectionsAsync() > terminalCorrectionCount
            && await realConnect.IsConnectorPausedAsync(name), "Worker 实际纠正 Completed 后迟到 resume");
        Assert.AreEqual(EventDeliveryRollbackPhase.Completed, (await LatestAsync()).Phase);
        await finalApi.CrashAsync();
        var resumes = control.ResumeCount;
        lost.DropResponse = true; lost.Release.TrySetResult(); await IgnoreLostResponseAsync(final);
        await RecoverAsync(completed.Generation); await RecoverAsync(completed.Generation);
        Assert.AreEqual(resumes, control.ResumeCount, "Completed 的重试绝不恢复旧 Connector。");
        Assert.IsTrue(await realConnect.IsConnectorPausedAsync(name)); Assert.AreEqual(0, await FenceAsync());
        var end = await BrokerEndAsync(); await CreateAsync("legacy-after");
        Assert.AreEqual(end, await BrokerEndAsync());
        Assert.AreEqual(units.Count, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_identity_organization_unit_projection WHERE TenantId=@Tenant AND UnitId IN @Ids", new { Tenant = tenant.TenantId, Ids = units.Select(item => item.Id).ToArray() }));
        var actor = await connection.QuerySingleAsync<Guid>("SELECT Id FROM fn_identity_user WHERE TenantId IS NULL AND NormalizedUsername='ADMIN'");
        Assert.IsGreaterThan(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit"), "真实运维操作必须留下 B0 审计，空表不能通过。");
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_domain_audit WHERE ActorUserId IS NULL OR ActorUserId<>@Actor", new { Actor = actor }),
            "所有进程的操作审计均应关联实际认证的宿主管理员。");
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_stream_ownership WHERE CurrentOwner<>0 OR RollbackState<>0"));
        await realConnect.DeleteConnectorAsync(name); await realConnect.DeleteConnectorAsync(shadowName);
        if (MessagingTestProcess.NativeEnabled)
        {
            await dlqTakeoverA.StopGracefullyAsync(); await dlqTakeoverB.StopGracefullyAsync();
        }
        Step(database, "process recovery assertions complete");

        async Task RegisterAsync(string connectorName, bool formal)
        {
            var config = await MessagingCutoverRollbackDebeziumTests.ConnectorAsync(database, pipeline, formal);
            config["schema.history.internal.kafka.topic"] += "." + connectorName;
            if (formal)
            {
                config["transforms.outbox.route.topic.replacement"] = Topic;
                config["database.port"] = connectorDatabaseWire.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var isolated = new Dictionary<string, string>(CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(connectorName, config), StringComparer.Ordinal);
            if (soak || automaticTtlTarget is not null)
            {
                ConfigureDedicatedStream(isolated, database.Provider, source, EventType, 1, formal ? Topic : Shadow, formal);
            }
            await realConnect.RegisterConnectorAsync(connectorName, isolated);
            Assert.IsTrue(await realConnect.WaitForConnectorHealthyAsync(connectorName, TimeSpan.FromSeconds(120)));
        }
        Task<MessagingTestProcess> WorkerAsync(string instance)
        {
            // 控制面按精确 ClientId 绑定部署，多个实例用 Broker 分配的 ConsumerId 区分，不放宽身份匹配。
            var worker = new Dictionary<string, string?>(settings) { ["Messaging:Worker:Mode"] = "HybridKafka", ["Messaging:Kafka:ClientId"] = "fullnet.multiprocess.worker" };
            return MessagingTestProcess.StartAsync("Worker", instance, database, worker);
        }
        async Task CreateAsync(string phase)
        {
            var unit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, tenant.AccessToken, "process-" + Guid.NewGuid().ToString("N"), phase, default);
            units.Add(unit); await WaitAsync(() => ProjectionAsync(unit), "真实业务投影 " + phase);
        }
        Task<bool> ProjectionAsync(OrganizationUnitResponse unit) => OrganizationUnitCdcKafkaEndToEndSupport.ProjectionExistsAsync(database, tenant.TenantId, unit.Id, unit.Name);
        async Task<RollbackOperationResponse> LatestAsync() => (await clientB.GetFromJsonAsync<RollbackOperationResponse>($"/api/v1/messaging/delivery/rollback-operations?eventType={EventType}&schemaVersion=1"))!;
        async Task RecoverAsync(Guid generation) => await PostAsync<RollbackOperationResponse>(clientB, $"/api/v1/messaging/delivery/rollback-operations/{generation}/recover", new RecoverRollbackRequest("same durable operation"));
        Task<int> FenceAsync() => connection.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@EventType AND SchemaVersion=1", new { EventType });
        async Task<int> CorrectionsAsync()
        {
            var directory = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!;
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "Worker-*.log"))
            {
                // 日志仍由本轮 Worker 写入；读句柄必须允许已存在的写句柄继续工作。
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                count += (await reader.ReadToEndAsync()).Split("Messaging control target was reconciled from durable terminal state.").Length - 1;
            }
            return count;
        }
        async Task<long> BrokerEndAsync() => (await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec { TopicPartition = new(Topic, 0), OffsetSpec = OffsetSpec.Latest() }])).ResultInfos.Single().TopicPartitionOffsetError.Offset.Value;
        async Task<long> GroupOffsetAsync() => (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(Group, [new TopicPartition(Topic, 0)])])).Single().Partitions.Single().Offset.Value;
    }

    private static ChangeDeliveryOwnerRequest Change(EventDeliveryOwner owner) => new(EventType, 1, owner, "real multi-process verification");

    /// <summary>按每条流隔离原始表过滤器和固定路由，避免同表其他类型或版本被复制到本流。</summary>
    internal static void ConfigureDedicatedStream(Dictionary<string, string> config, DatabaseProvider provider,
        string sourceDatabase, string eventType, int schemaVersion, string targetTopic, bool formal)
    {
        if (schemaVersion <= 0 || string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(targetTopic)
            || string.IsNullOrWhiteSpace(sourceDatabase) || !config.TryGetValue("topic.prefix", out var prefix)
            || string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("独立流测试必须提供完整的事件、源库和 Topic 身份。");
        config["transforms"] = "fullnet_filter,outbox";
        config["transforms.fullnet_filter.type"] = "net.full.connect.FullNetOutboxStreamFilter";
        config["transforms.fullnet_filter.source.topic"] = prefix + "." + sourceDatabase
            + (provider == DatabaseProvider.SqlServer ? ".dbo." : ".")
            + (formal ? "fn_messaging_outbox_event" : "fn_messaging_shadow_event");
        config["transforms.fullnet_filter.event.type"] = eventType;
        config["transforms.fullnet_filter.schema.version"] = schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        config["transforms.outbox.route.topic.replacement"] = targetTopic;
    }

    /// <summary>失败诊断只保留运行状态，不输出 Connect trace 或含凭据的原始配置。</summary>
    private static async Task<string[]> ReadConnectorStatesAsync(KafkaConnectAdminClient connect, string name)
    {
        var json = await connect.TryGetConnectorStatusAsync(name);
        if (json is null) return ["unavailable"];
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("state").GetString() ?? "unknown")
            .Prepend(root.GetProperty("connector").GetProperty("state").GetString() ?? "unknown").ToArray();
    }

    private static async Task KillControlSessionAsync(DbConnection connection, DatabaseProvider provider)
    {
        var id = provider == DatabaseProvider.SqlServer
            ? await connection.QuerySingleAsync<int>("SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type='APPLICATION' AND request_owner_type='SESSION' AND resource_database_id=DB_ID() AND resource_description LIKE '%messaging.delivery_control%'")
            : await connection.QuerySingleAsync<int>("SELECT IS_USED_LOCK('messaging.delivery_control')");
        Assert.IsGreaterThan(0, id);
        // 会话 ID 来自独享数据库的持锁查询；只杀该锁连接，API 与其余数据库连接保持运行。
        await connection.ExecuteAsync("KILL " + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static async Task RestartTasksAsync(Uri address, string name, bool onlyFailed)
    {
        using var client = new HttpClient { BaseAddress = address };
        using var response = await client.PostAsync($"/connectors/{name}/restart?includeTasks=true&onlyFailed={onlyFailed.ToString().ToLowerInvariant()}", null);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, "本轮独享 Connector 的实际任务重启。");
    }
    private static bool HasFailedTask(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        using var document = System.Text.Json.JsonDocument.Parse(status);
        return document.RootElement.TryGetProperty("tasks", out var tasks)
            && tasks.EnumerateArray().Any(task => task.TryGetProperty("state", out var state) && state.GetString() == "FAILED");
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path + ": " + await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<HttpResponseMessage> PostControlAsync(HttpClient client, string path, object body)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.PostAsJsonAsync(path, body);
            if (response.StatusCode != HttpStatusCode.Conflict || attempt >= 19) return response;
            response.Dispose(); await Task.Delay(250);
        }
    }
    private static async Task AwaitGateAsync(MessagingHttpFaultProxy.Gate gate, Task<HttpResponseMessage> request)
    {
        await Task.WhenAny(gate.Reached.Task, request).WaitAsync(TimeSpan.FromMinutes(5));
        if (!gate.Reached.Task.IsCompleted)
        {
            using var response = await request;
            Assert.Fail("请求在实际故障点之前返回：" + response.StatusCode + " " + await response.Content.ReadAsStringAsync());
        }
    }
    private static async Task IgnoreLostResponseAsync(Task<HttpResponseMessage> request)
    {
        try { using var response = await request; Assert.Fail("崩溃或响应丢失请求不应返回完成响应：" + response.StatusCode); }
        catch (HttpRequestException) { }
    }
    private static async Task WaitAsync(Func<Task<bool>> predicate, string condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { while (!await predicate()) await Task.Delay(300, timeout.Token); }
        catch (OperationCanceledException) { Assert.Fail("等待失败：" + condition); }
    }
    private void Step(DatabaseOptions database, string text)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {database.Provider}: {text}";
        TestContext.WriteLine(line);
        if (Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR") is { } directory)
            File.AppendAllText(Path.Combine(directory, "process-phases.log"), line + Environment.NewLine);
    }
}
