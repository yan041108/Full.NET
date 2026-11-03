using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.IntegrationTests.Messaging;

public sealed partial class MessagingMultiProcessRecoveryTests
{
    private static string? AutomaticTtlTarget(bool soak)
    {
        var target = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_AUTOMATIC_TTL");
        if (string.IsNullOrEmpty(target)) return null;
        if (!MessagingTestProcess.NativeEnabled || soak || target is not ("formal" or "retry")
            || Environment.GetEnvironmentVariable("FULLNET_MESSAGING_ACCEPTANCE_STAGE") != "component-native-auto-ttl-" + target)
            throw new InvalidOperationException("自动 TTL 场景只允许明确的 Native formal/retry 控制面入口。");
        return target;
    }

    /// <summary>只调整本轮独占 Topic 的保留时间，等待真实日志清理；禁止用手工截断或重建代替 TTL。</summary>
    private async Task AutomaticTtlAsync(string target, DatabaseOptions database, string databaseId, DbConnection connection,
        CdcDebeziumPipelineEnvironment pipeline, IAdminClient broker, KafkaMessagingOptions kafka,
        KafkaConnectAdminClient realConnect, MessagingHttpFaultProxy control, string connector,
        HttpClient client, HttpClient business, CdcKafkaDeliveryBinding topology, string topologyJson,
        Func<Task<RollbackOperationResponse>> latest, Func<Task<int>> fence, MessagingIndependentStreamProbe independentProbe)
    {
        const string route = "/api/v1/messaging/kafka/replay-operations";
        var targetTopic = target == "formal" ? Topic : KafkaTopicNames.GetRetryTopic(Topic, kafka.RetryStages[0]);
        var partition = new TopicPartition(targetTopic, 0);
        var observer = new KafkaConsumerLagObserver();
        var brokerConfiguration = (await broker.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Broker, Name = "1" }])).Single().Entries;
        Assert.AreEqual("1000", brokerConfiguration["log.retention.check.interval.ms"].Value, "必须读取真实 Broker 清理周期，不能把请求配置当作生效证据。");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var baseline = topology.Topics.Single(item => item.TopicName == targetTopic);
        var effectsBeforeDuplicate = await ProjectionAsync();
        if (target == "retry")
        {
            // 复用已确认的真实正式信封；这只制造保留窗口，不把它冒充新的业务重试故障。
            var formalEnd = await EndAsync(new(Topic, 0));
            using var reader = CdcDebeziumE2ESupport.CreateShadowConsumer(pipeline, "fullnet.ttl.copy." + Guid.NewGuid().ToString("N"));
            reader.Assign(new TopicPartitionOffset(new TopicPartition(Topic, 0), formalEnd - 1));
            var actual = reader.Consume(TimeSpan.FromSeconds(10)); Assert.IsNotNull(actual);
            using var producer = new ProducerBuilder<string, byte[]>(kafka.BuildProducerConfig()).Build();
            await producer.ProduceAsync(partition, new Message<string, byte[]> { Key = actual.Message.Key,
                Value = actual.Message.Value, Headers = KafkaDeliveryHeaders.CloneHeaders(actual.Message.Headers) }, deadline.Token);
            reader.Close();
        }
        var oldHigh = await EndAsync(partition); Assert.IsGreaterThan(0L, oldHigh);
        await WaitAsync(async () => await GroupAsync(partition) == oldHigh, "TTL 前目标消费组已确认真实窗口");
        Assert.AreEqual(effectsBeforeDuplicate, await ProjectionAsync(), "重试窗口副本不得改变业务投影。");
        var formalHigh = await EndAsync(new(Topic, 0));
        await WaitAsync(async () => await GroupAsync(new(Topic, 0)) == formalHigh, "TTL 前正式消费组提交稳定");
        var formalCommitted = await GroupAsync(new(Topic, 0));
        var originalProjection = await ProjectionAsync();
        var outboxCount = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@EventType", new { EventType });
        KafkaReplayOperationResponse? replay = null;
        string? replayRequestJson = null;
        string? replaySnapshotJson = null;
        KafkaReplayOperationResponse? replayAfterTtl = null;
        if (target == "formal")
        {
            Assert.IsTrue(oldHigh >= 2, "部分重放必须有两个实际已处理事件，不用合成信封填充。");
            var firstOffset = (oldHigh - 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var offset = (oldHigh - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var created = await client.PostAsJsonAsync(route, new CreateKafkaReplayOperationRequest(Guid.CreateVersion7(), Topic,
                null, null, firstOffset, offset, [0], Group, 1, "automatic TTL fixed offset window"));
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            replay = (await created.Content.ReadFromJsonAsync<KafkaReplayOperationResponse>())!;
            // 真正执行一次有界重放并保存断点；TTL 后必须恢复原快照，不能从当前 Low 重新取窗。
            using var partial = await client.PostAsJsonAsync($"{route}/{replay.OperationId}/execute", new ChangeKafkaReplayOperationRequest("establish actual partial replay checkpoint"));
            Assert.AreEqual(HttpStatusCode.OK, partial.StatusCode, await partial.Content.ReadAsStringAsync());
            replay = (await partial.Content.ReadFromJsonAsync<KafkaReplayOperationResponse>())!;
            Assert.AreEqual("partial", replay.State); Assert.AreEqual(1L, replay.ConfirmedMessages);
            Assert.AreEqual(0L, replay.ProcessedMessages); Assert.AreEqual(1L, replay.AlreadyProcessedMessages);
            Assert.IsFalse(replay.HasUnconfirmedMessage); Assert.AreEqual(0, replay.Partitions.Single().Partition);
            Assert.AreEqual(firstOffset, replay.Partitions.Single().StartOffset);
            Assert.AreEqual(oldHigh.ToString(System.Globalization.CultureInfo.InvariantCulture), replay.Partitions.Single().EndOffsetExclusive);
            Assert.AreEqual(offset, replay.Partitions.Single().NextOffset);
            replaySnapshotJson = await connection.QuerySingleAsync<string>("SELECT SnapshotJson FROM fn_messaging_replay_operation WHERE Id=@Id", new { Id = replay.OperationId });
            Assert.IsFalse(string.IsNullOrWhiteSpace(replaySnapshotJson));
            Assert.AreEqual(originalProjection, await ProjectionAsync(), "TTL 前去重重放也不得增加投影副作用。");
            replayRequestJson = await connection.QuerySingleAsync<string>("SELECT RequestJson FROM fn_messaging_replay_operation WHERE Id=@Id", new { Id = replay.OperationId });
        }
        Step(database, "automatic TTL " + target + ": pause actual Connector behind durable fence");
        var gate = control.Arm("PUT", $"/connectors/{connector}/pause", afterExecution: true);
        var rollback = PostControlAsync(client, "/api/v1/messaging/delivery/rollback", Change(EventDeliveryOwner.LegacyPolling));
        var waitForPause = AwaitGateAsync(gate, rollback);
        if (database.Provider == DatabaseProvider.SqlServer)
        {
            // 仅在真实控制调用滞留时采样，区分 CDC 捕获未追上与 Connector 提交位点未越过边界。
            for (var sample = 0; sample < 2 && !waitForPause.IsCompleted; sample++)
            {
                if (await Task.WhenAny(waitForPause, Task.Delay(TimeSpan.FromSeconds(30))) == waitForPause) break;
                var counts = await connection.QuerySingleAsync<(long SourceCount, long CapturedCount, byte[]? CapturedLsn)>("""
                    SELECT (SELECT COUNT_BIG(*) FROM fn_messaging_outbox_event WHERE MessageType=@EventType AND SchemaVersion=1),
                        COUNT_BIG(DISTINCT changes.Id), CAST(MAX(changes.__$start_lsn) AS varbinary(10))
                    FROM cdc.fullnet_fn_messaging_outbox_event_CT changes
                    INNER JOIN fn_messaging_outbox_event source ON source.Id=changes.Id
                    WHERE source.MessageType=@EventType AND source.SchemaVersion=1 AND changes.__$operation=2
                    """, new { EventType });
                var phase = await connection.QuerySingleOrDefaultAsync<int?>("""
                    SELECT TOP 1 Phase FROM fn_messaging_rollback_operation
                    WHERE EventType=@EventType AND SchemaVersion=1 ORDER BY CreatedAtUtc DESC, Id DESC
                    """, new { EventType });
                var position = await realConnect.TryReadConnectorPositionAsync(connector);
                await File.AppendAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, "ttl-pause-diagnostics.ndjson"),
                    JsonSerializer.Serialize(new { Provider = "SqlServer", Sample = sample, counts.SourceCount, counts.CapturedCount,
                        CapturedLsn = counts.CapturedLsn is null ? null : Convert.ToHexString(counts.CapturedLsn),
                        ConnectorLsn = position?.Lsn?.CommitLsn, Phase = phase }) + Environment.NewLine);
            }
        }
        await waitForPause;
        await WaitAsync(() => realConnect.IsConnectorPausedAsync(connector), "TTL 过期前实际 Connector PAUSED");
        var prepared = await latest(); Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, prepared.Phase);
        Assert.AreEqual(1, await fence());
        var resourcesJson = await ResourcesAsync(prepared.Generation); Assert.IsFalse(string.IsNullOrWhiteSpace(resourcesJson));
        var resumeCount = control.ResumeCount;
        var connectorConfiguration = (await realConnect.TryReadConnectorConfigurationAsync(connector))!;
        var fingerprint = KafkaConnectResourceIdentity.Compute(connectorConfiguration);
        var timer = Stopwatch.StartNew();
        var expiryElapsed = 0d;
        CdcKafkaTopicBinding? expired = null;
        try
        {
            await broker.IncrementalAlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [new() { Type = ResourceType.Topic, Name = targetTopic }] =
                [new() { Name = "retention.ms", Value = "2000", IncrementalOperation = AlterConfigOpType.Set }],
            });
            await WaitAsync(async () =>
            {
                var observed = await observer.ObserveTopicBindingsAsync(kafka, [targetTopic], deadline.Token);
                expired = observed?.Single();
                return expired is not null && expired.Partitions.Single().Low >= oldHigh;
            }, "Broker 自动 retention 清理实际推进 Low，覆盖原消息位点");
            expiryElapsed = timer.Elapsed.TotalSeconds;
            Assert.IsNotNull(expired);
            Assert.AreEqual(baseline.TopicId, expired.TopicId);
            Assert.AreEqual(1, expired.Partitions.Length);
            Assert.IsGreaterThan(baseline.Partitions.Single().Low, expired.Partitions.Single().Low);
            Assert.IsTrue(expired.Partitions.Single().High >= oldHigh);
            Assert.AreEqual(fingerprint, KafkaConnectResourceIdentity.Compute((await realConnect.TryReadConnectorConfigurationAsync(connector))!));
            await independentProbe.VerifyAsync("main-connector-paused-and-expired");
        }
        finally { gate.Release.TrySetResult(); }
        using (var response = await rollback)
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, await response.Content.ReadAsStringAsync());
        await AssertProtectedAsync(prepared.Generation);
        using (var response = await client.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{prepared.Generation}/recover",
            new RecoverRollbackRequest("automatic TTL must not replace original resource window")))
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, await response.Content.ReadAsStringAsync());
        await AssertProtectedAsync(prepared.Generation);
        if (replay is not null)
        {
            using var blocked = await client.PostAsJsonAsync($"{route}/{replay.OperationId}/execute", new ChangeKafkaReplayOperationRequest("automatic TTL removed fixed offsets"));
            Assert.AreEqual(HttpStatusCode.Conflict, blocked.StatusCode, await blocked.Content.ReadAsStringAsync());
            var state = (await client.GetFromJsonAsync<KafkaReplayOperationResponse>($"{route}/{replay.OperationId}"))!;
            Assert.AreEqual("blocked", state.State); Assert.AreEqual("resource_or_window_changed", state.LastReasonCode);
            Assert.AreEqual(1L, state.ConfirmedMessages);
            Assert.AreEqual(0L, state.ProcessedMessages); Assert.AreEqual(1L, state.AlreadyProcessedMessages);
            Assert.IsFalse(state.HasUnconfirmedMessage);
            Assert.AreEqual(replay.Partitions.Single().NextOffset, state.Partitions.Single().NextOffset);
            Assert.IsTrue(replaySnapshotJson == await connection.QuerySingleAsync<string>("SELECT SnapshotJson FROM fn_messaging_replay_operation WHERE Id=@Id", new { Id = replay.OperationId }), "过期后不得重新绑定当前窗口或移动旧断点。");
            replayAfterTtl = state;
            Assert.AreEqual(replayRequestJson, await connection.QuerySingleAsync<string>("SELECT RequestJson FROM fn_messaging_replay_operation WHERE Id=@Id", new { Id = replay.OperationId }));
        }
        var code = "ttl-fenced-" + Guid.NewGuid().ToString("N");
        using (var rejected = await business.PostAsJsonAsync("/api/v1/organization/units", new CreateOrganizationUnitRequest(null, code, "TTL fenced", 10)))
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_organization_unit WHERE Code=@code", new { code }));
        // 跨过 Worker 的十秒巡检周期，确认它不会用当前窗口改写原冻结资源或解除 fence。
        await Task.Delay(TimeSpan.FromSeconds(12));
        await AssertProtectedAsync(prepared.Generation);
        Assert.AreEqual(formalCommitted, await GroupAsync(new(Topic, 0)), "拒绝重放和恢复不得移动正式组位点。");
        Assert.AreEqual(originalProjection, await ProjectionAsync());
        Assert.AreEqual(outboxCount, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@EventType", new { EventType }));
        var configs = (await broker.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Topic, Name = targetTopic }])).Single().Entries;
        foreach (var (key, expected) in new[] { ("cleanup.policy", "delete"), ("retention.ms", "2000"), ("retention.bytes", "-1"), ("segment.ms", "1000"), ("message.timestamp.type", "LogAppendTime") })
            Assert.AreEqual(expected, configs[key].Value);
        await File.AppendAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, "automatic-ttl.ndjson"),
            JsonSerializer.Serialize(new { Provider = database.Provider.ToString(), Target = target, ExecutionKind = "native_linux",
                OwnedInfrastructureContainers = new[] { databaseId, pipeline.OwnedContainerIds[0], pipeline.OwnedContainerIds[1] },
                TargetTopic = targetTopic, baseline.TopicId, OriginalLow = baseline.Partitions.Single().Low, OldHigh = oldHigh,
                ExpiredLow = expired!.Partitions.Single().Low, ExpiredHigh = expired.Partitions.Single().High,
                AutomaticExpirySeconds = expiryElapsed, RetentionMs = 2000, SegmentMs = 1000, BrokerCleanupIntervalMs = 1000,
                OriginalResourcesPreserved = true, ProducerFence = 1, Owner = "CdcKafka", Phase = "Recovering", ResumeDelta = control.ResumeCount - resumeCount,
                OriginalActivationPreserved = true, FixedReplayWindowBlocked = replay is not null, FormalGroupOffsetUnchanged = true,
                PartialReplayRecoveryBlocked = replayAfterTtl is not null, OriginalReplayConfirmedMessages = replay?.ConfirmedMessages,
                ReplayConfirmedMessagesAfterTtl = replayAfterTtl?.ConfirmedMessages, OriginalReplayNextOffset = replay?.Partitions.Single().NextOffset,
                ReplayNextOffsetAfterTtl = replayAfterTtl?.Partitions.Single().NextOffset, ReplaySnapshotUnchanged = replayAfterTtl is not null,
                BusinessWriteStatus = 503, ProjectionUnchanged = true, OutboxUnchanged = true }) + Environment.NewLine);

        async Task AssertProtectedAsync(Guid generation)
        {
            var current = await latest(); Assert.AreEqual(generation, current.Generation);
            Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, current.Phase);
            Assert.AreEqual(1, await fence()); Assert.AreEqual(resumeCount, control.ResumeCount);
            Assert.IsTrue(await realConnect.IsConnectorPausedAsync(connector));
            Assert.IsTrue(resourcesJson == await ResourcesAsync(generation), "不得重写原冻结资源。");
            Assert.IsTrue(topologyJson == await connection.QuerySingleAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@EventType AND SchemaVersion=1", new { EventType }), "不得替换原切流激活基线。");
            Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@EventType AND SchemaVersion=1", new { EventType }));
        }
        Task<string> ResourcesAsync(Guid generation) => connection.QuerySingleAsync<string>("SELECT ResourcesJson FROM fn_messaging_rollback_operation WHERE Id=@generation", new { generation });
        async Task<long> EndAsync(TopicPartition selected) => (await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec { TopicPartition = selected, OffsetSpec = OffsetSpec.Latest() }])).ResultInfos.Single().TopicPartitionOffsetError.Offset.Value;
        async Task<long> GroupAsync(TopicPartition selected) => (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(Group, [selected])])).Single().Partitions.Single().Offset.Value;
        async Task<string> ProjectionAsync() => JsonSerializer.Serialize((await connection.QueryAsync("SELECT TenantId, UnitId, Name, IsActive, SourceVersion, SourceUpdatedAtUtc, ProjectedAtUtc FROM fn_identity_organization_unit_projection ORDER BY TenantId, UnitId")).Select(row => (IDictionary<string, object>)row));
    }
}
