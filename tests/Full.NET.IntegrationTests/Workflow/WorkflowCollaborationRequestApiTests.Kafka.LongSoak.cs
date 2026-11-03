using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    /// <summary>通过正式业务操作持续产生四条已切流事件，观测双 Worker、实际 Connector 和组位点。</summary>
    private static async Task RunRealBusinessStreamsSoakAsync(DbConnection database, IAdminClient broker,
        KafkaConnectAdminClient connect, (string EventType, string Topic)[] streams, string group,
        string connectorPrefix, string evidenceDirectory, int seconds, Func<bool, Task> runBusiness,
        Func<Task> restartWorker, Func<string[]> workerContainers)
    {
        Assert.IsTrue(seconds is 60 or 3600);
        Assert.AreEqual(2, workerContainers().Distinct(StringComparer.Ordinal).Count(), "必须记录两个独立 Worker 容器。");
        var evidence = Path.Combine(evidenceDirectory, "real-business-stream-soak.ndjson");
        var targets = streams.SelectMany(stream => new[] { stream.Topic }.Concat(new KafkaMessagingOptions().RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(stream.Topic, stage))))
            .Select(topic => new TopicPartition(topic, 0)).ToArray();
        await WaitMembersAsync();
        var baseline = await CountsAsync();
        var timer = Stopwatch.StartNew(); var nextSample = TimeSpan.Zero; var iterations = 0; var restarted = false;
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await runBusiness(iterations % 2 != 0);
            iterations++;
            if (!restarted && timer.Elapsed >= TimeSpan.FromSeconds(seconds / 2d))
            {
                await restartWorker();
                await WaitMembersAsync();
                restarted = true;
            }
            if (timer.Elapsed >= nextSample)
            {
                var snapshot = await SampleAsync();
                await File.AppendAllTextAsync(evidence, JsonSerializer.Serialize(new { ElapsedSeconds = timer.Elapsed.TotalSeconds,
                    Iterations = iterations, WorkerMembers = 2, Restarted = restarted, snapshot.Streams,
                    snapshot.Positions, snapshot.WorkerResources }) + Environment.NewLine);
                nextSample = timer.Elapsed + TimeSpan.FromSeconds(30);
            }
            if (timer.Elapsed < TimeSpan.FromSeconds(seconds)) await Task.Delay(TimeSpan.FromSeconds(5));
        }
        Assert.IsTrue(restarted, "长稳期间必须实际重启第二个 Worker。");
        await WaitKafkaAsync(async () =>
        {
            foreach (var partition in targets)
            {
                var end = (await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec
                    { TopicPartition = partition, OffsetSpec = OffsetSpec.Latest() }])).ResultInfos.Single().TopicPartitionOffsetError;
                var start = (await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec
                    { TopicPartition = partition, OffsetSpec = OffsetSpec.Earliest() }])).ResultInfos.Single().TopicPartitionOffsetError;
                var committed = (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group, [partition])])).Single().Partitions.Single();
                if (end.Error.IsError || start.Error.IsError || committed.Error.IsError) return false;
                // 从未写入的空分区允许尚无提交位点；保留窗口非空时绝不把未知位点当作零。
                if (committed.Offset.Value < end.Offset.Value && !(committed.Offset.Value < 0 && start.Offset.Value == 0 && end.Offset.Value == 0)) return false;
            }
            return true;
        }, "真实多流消费组排空全部正式及重试 Topic");
        var final = await SampleAsync();
        for (var index = 0; index < streams.Length; index++)
        {
            Assert.IsGreaterThan(baseline[index].Outbox, final.Streams[index].Outbox, "每条业务流必须在长稳期间实际产生新事件。");
            Assert.IsGreaterThan(baseline[index].Processed, final.Streams[index].Processed, "每条业务流必须在长稳期间实际进入 Inbox。");
            Assert.AreEqual(final.Streams[index].Outbox, final.Streams[index].Processed, "长稳结束时目标流真实 Outbox 必须全部处理。");
            Assert.AreEqual(0L, final.Streams[index].Failed, "长稳结束不能遗留失败 Inbox。");
        }
        await File.AppendAllTextAsync(evidence, JsonSerializer.Serialize(new { Complete = true, RequestedSeconds = seconds,
            ElapsedSeconds = timer.Elapsed.TotalSeconds, Iterations = iterations, WorkerMembers = 2,
            Restarted = restarted, SingleWorkerGroupObserved = restarted, BusinessSucceededDuringSingleWorker = restarted,
            Baseline = baseline, Final = final.Streams, final.Positions, final.WorkerResources }) + Environment.NewLine);

        async Task<BusinessStreamCount[]> CountsAsync()
        {
            var values = new List<BusinessStreamCount>();
            foreach (var stream in streams)
            {
                var type = stream.EventType;
                var outbox = await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@type AND SchemaVersion=1", new { type });
                var processed = await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type AND SchemaVersion=1 AND Status='processed'", new { group, type });
                var failed = await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type AND SchemaVersion=1 AND Status='failed'", new { group, type });
                var owner = await database.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type });
                values.Add(new(stream.EventType, stream.Topic, outbox, processed, failed, owner));
            }
            return values.ToArray();
        }

        Task WaitMembersAsync() => WaitKafkaAsync(async () =>
        {
            var state = (await broker.DescribeConsumerGroupsAsync([group])).ConsumerGroupDescriptions.Single();
            return state.State == ConsumerGroupState.Stable && state.Members.Count == 2
                && state.Members.All(member => member.Assignment.TopicPartitions.Count > 0)
                && targets.All(state.Members.SelectMany(member => member.Assignment.TopicPartitions).Contains);
        }, "两个真实 Worker 的消费组稳定");

        async Task<BusinessStreamSample> SampleAsync()
        {
            var state = (await broker.DescribeConsumerGroupsAsync([group])).ConsumerGroupDescriptions.Single();
            Assert.AreEqual(ConsumerGroupState.Stable, state.State);
            Assert.AreEqual(2, state.Members.Count, "必须持续确认两个真实 Worker 成员。");
            Assert.IsTrue(state.Members.All(member => member.Assignment.TopicPartitions.Count > 0), "两个 Worker 都必须持有真实分区。");
            var assignments = state.Members.SelectMany(member => member.Assignment.TopicPartitions).ToArray();
            Assert.AreEqual(assignments.Length, assignments.Distinct().Count(), "分区不能重复分配。");
            Assert.IsTrue(targets.All(assignments.Contains), "四条正式业务流及其重试分区必须均已分配。");
            for (var index = 0; index < streams.Length; index++)
                Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connectorPrefix + "-" + index, TimeSpan.FromSeconds(15)), "每条正式 Connector 必须健康。");
            var values = await CountsAsync();
            Assert.IsTrue(values.All(value => value.Owner == 2), "四条真实业务流必须保持 CDC 所有权。");
            var highs = (await broker.ListOffsetsAsync(targets.Select(part => new TopicPartitionOffsetSpec
                { TopicPartition = part, OffsetSpec = OffsetSpec.Latest() }).ToArray())).ResultInfos;
            var lows = (await broker.ListOffsetsAsync(targets.Select(part => new TopicPartitionOffsetSpec
                { TopicPartition = part, OffsetSpec = OffsetSpec.Earliest() }).ToArray())).ResultInfos;
            var offsets = (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group, targets.ToList())])).Single().Partitions;
            Assert.IsTrue(offsets.All(item => !item.Error.IsError), "不得用失败的消费组查询代替位点观测。");
            var positions = targets.Select(part => new BusinessStreamPosition(part.Topic,
                lows.Single(item => item.TopicPartitionOffsetError.TopicPartition.Equals(part)).TopicPartitionOffsetError.Offset.Value,
                highs.Single(item => item.TopicPartitionOffsetError.TopicPartition.Equals(part)).TopicPartitionOffsetError.Offset.Value,
                offsets.Single(item => item.TopicPartition.Equals(part)).Offset.Value)).ToArray();
            Assert.IsTrue(lows.All(item => !item.TopicPartitionOffsetError.Error.IsError) && highs.All(item => !item.TopicPartitionOffsetError.Error.IsError), "不得用失败的水位查询代替观测。");
            var workerIds = workerContainers();
            Assert.AreEqual(2, workerIds.Distinct(StringComparer.Ordinal).Count(), "资源采样必须绑定两个独立 Worker。");
            return new(values, positions, await WorkerResourcesAsync(workerIds));
        }
    }

    private sealed record BusinessStreamCount(string EventType, string Topic, long Outbox, long Processed, long Failed, int Owner);
    private sealed record BusinessStreamPosition(string Topic, long Low, long High, long Committed);
    private sealed record BusinessWorkerResource(string ContainerId, string CpuPercent, string MemoryUsage, string Processes);
    private sealed record BusinessStreamSample(BusinessStreamCount[] Streams, BusinessStreamPosition[] Positions, BusinessWorkerResource[] WorkerResources);

    private static async Task<BusinessWorkerResource[]> WorkerResourcesAsync(string[] containerIds)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "stats", "--no-stream", "--format", "{{json .}}" }.Concat(containerIds)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Worker 资源采样。");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        Assert.AreEqual(0, process.ExitCode, "Worker 资源采样失败：" + await error);
        var rows = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.AreEqual(2, rows.Length, "必须分别采样两个 Worker 容器。");
        Assert.IsTrue(rows.All(row => containerIds.Any(id => id.StartsWith(row.GetProperty("ID").GetString()!, StringComparison.Ordinal))), "资源采样不得混入其他容器。");
        return rows.Select(row => new BusinessWorkerResource(row.GetProperty("ID").GetString()!, row.GetProperty("CPUPerc").GetString()!,
            row.GetProperty("MemUsage").GetString()!, row.GetProperty("PIDs").GetString()!)).ToArray();
    }

    /// <summary>真实业务流回退时等待 Broker 自动过期，同时用另一业务分支证明其他流继续处理。</summary>
    private static async Task RunRealBusinessStreamTtlAsync(DbConnection database, IAdminClient broker,
        KafkaConnectAdminClient connect, MessagingHttpFaultProxy control, HttpClient api,
        (string EventType, string Topic)[] streams, string group, string connectorPrefix, string bootstrap,
        string target, string evidenceDirectory, Func<Task> runIndependentBusiness,
        Func<Task> prepareFencedCompletion, Func<Task> assertFencedCompletion)
    {
        var selected = streams[2];
        var kafka = new KafkaMessagingOptions { Enabled = true, BootstrapServers = bootstrap,
            SecurityProtocol = "Plaintext", ClientId = "fullnet.workflow.ttl.observer" };
        var topic = target == "formal" ? selected.Topic : KafkaTopicNames.GetRetryTopic(selected.Topic, kafka.RetryStages[0]);
        var partition = new TopicPartition(topic, 0);
        var observer = new KafkaConsumerLagObserver();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var configs = (await broker.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Broker, Name = "1" }])).Single().Entries;
        Assert.AreEqual("1000", configs["log.retention.check.interval.ms"].Value);
        if (target == "retry")
        {
            var formal = new TopicPartition(selected.Topic, 0);
            var formalHigh = await HighAsync(formal);
            Assert.IsGreaterThan(0L, formalHigh);
            using var reader = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = bootstrap,
                GroupId = "fullnet.workflow.ttl-copy." + Guid.NewGuid().ToString("N"), EnableAutoCommit = false }).Build();
            reader.Assign(new TopicPartitionOffset(formal, formalHigh - 1));
            var record = reader.Consume(TimeSpan.FromSeconds(10)); Assert.IsNotNull(record);
            using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = bootstrap, EnableIdempotence = true }).Build();
            await producer.ProduceAsync(partition, new Message<string, byte[]> { Key = record.Message.Key,
                Value = record.Message.Value, Headers = KafkaDeliveryHeaders.CloneHeaders(record.Message.Headers) }, timeout.Token);
            reader.Close();
        }
        var before = (await observer.ObserveTopicBindingsAsync(kafka, [topic], timeout.Token))!.Single();
        var oldHigh = before.Partitions.Single().High;
        Assert.IsGreaterThan(0L, oldHigh, "目标窗口必须包含真实业务信封或其重试副本。");
        await WaitKafkaAsync(async () => await GroupOffsetAsync(partition) == oldHigh, "TTL 前实际消费组已确认目标窗口");
        KafkaReplayOperationResponse? replay = null; string? replaySnapshot = null; string? replayRequest = null; string? replayNext = null;
        if (target == "formal")
        {
            Assert.IsTrue(oldHigh >= 2, "部分重放必须包含至少两条真实业务事件。");
            var start = (oldHigh - 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var end = (oldHigh - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var created = await api.PostAsJsonAsync("/api/v1/messaging/kafka/replay-operations",
                new CreateKafkaReplayOperationRequest(Guid.CreateVersion7(), topic, null, null, start, end, [0], group, 1, "real business multistream TTL replay"));
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            replay = (await created.Content.ReadFromJsonAsync<KafkaReplayOperationResponse>())!;
            using var partial = await api.PostAsJsonAsync($"/api/v1/messaging/kafka/replay-operations/{replay.OperationId}/execute",
                new ChangeKafkaReplayOperationRequest("establish fixed replay checkpoint"));
            Assert.AreEqual(HttpStatusCode.OK, partial.StatusCode, await partial.Content.ReadAsStringAsync());
            replay = (await partial.Content.ReadFromJsonAsync<KafkaReplayOperationResponse>())!;
            Assert.AreEqual("partial", replay.State); Assert.AreEqual(1L, replay.ConfirmedMessages);
            Assert.AreEqual(1L, replay.AlreadyProcessedMessages); Assert.AreEqual(0L, replay.ProcessedMessages);
            Assert.IsFalse(replay.HasUnconfirmedMessage);
            replayNext = replay.Partitions.Single().NextOffset;
            replaySnapshot = await database.QuerySingleAsync<string>("SELECT SnapshotJson FROM fn_messaging_replay_operation WHERE Id=@id", new { id = replay.OperationId });
            replayRequest = await database.QuerySingleAsync<string>("SELECT RequestJson FROM fn_messaging_replay_operation WHERE Id=@id", new { id = replay.OperationId });
        }
        var targetOutbox = await CountOutboxAsync(selected.EventType);
        var targetInbox = await CountInboxAsync(selected.EventType);
        var independentBefore = new long[streams.Length];
        foreach (var index in new[] { 0, 1, 3 }) independentBefore[index] = await CountInboxAsync(streams[index].EventType);
        var binding = await database.QuerySingleAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = selected.EventType });
        var connector = connectorPrefix + "-2";
        var gate = control.Arm("PUT", $"/connectors/{connector}/pause", afterExecution: true);
        var rollback = ControlAsync(api, "/api/v1/messaging/delivery/rollback",
            new ChangeDeliveryOwnerRequest(selected.EventType, 1, EventDeliveryOwner.LegacyPolling, "real business multistream TTL"));
        var resumeCount = control.ResumeCount;
        try
        {
            await ReachedAsync(gate, rollback);
            await WaitKafkaAsync(() => connect.IsConnectorPausedAsync(connector), "自动过期前主 Connector 实际暂停");
            var operation = (await api.GetFromJsonAsync<RollbackOperationResponse>(
                "/api/v1/messaging/delivery/rollback-operations?eventType=" + selected.EventType + "&schemaVersion=1"))!;
            Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, operation.Phase);
            var resources = await database.QuerySingleAsync<string>("SELECT ResourcesJson FROM fn_messaging_rollback_operation WHERE Id=@id", new { id = operation.Generation });
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources));
            await prepareFencedCompletion();
            await broker.IncrementalAlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [new() { Type = ResourceType.Topic, Name = topic }] =
                [new() { Name = "retention.ms", Value = "2000", IncrementalOperation = AlterConfigOpType.Set }],
            });
            CdcKafkaTopicBinding? expired = null;
            await WaitKafkaAsync(async () =>
            {
                expired = (await observer.ObserveTopicBindingsAsync(kafka, [topic], timeout.Token))?.Single();
                return expired is not null && expired.Partitions.Single().Low >= oldHigh;
            }, "真实 Broker 自动清理目标窗口");
            Assert.IsNotNull(expired); Assert.AreEqual(before.TopicId, expired.TopicId);
            await runIndependentBusiness();
            foreach (var index in new[] { 0, 1, 3 })
            {
                Assert.IsGreaterThan(independentBefore[index], await CountInboxAsync(streams[index].EventType), "暂停期间其他真实业务流必须继续进入 Inbox。");
                Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connectorPrefix + "-" + index, TimeSpan.FromSeconds(15)));
            }
            Assert.AreEqual(targetOutbox, await CountOutboxAsync(selected.EventType));
            Assert.AreEqual(targetInbox, await CountInboxAsync(selected.EventType));
            await assertFencedCompletion();
            gate.Release.TrySetResult();
            using (var response = await rollback) Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, await response.Content.ReadAsStringAsync());
            using (var recover = await api.PostAsJsonAsync($"/api/v1/messaging/delivery/rollback-operations/{operation.Generation:D}/recover",
                new RecoverRollbackRequest("expired real business stream window")))
                Assert.AreEqual(HttpStatusCode.InternalServerError, recover.StatusCode, await recover.Content.ReadAsStringAsync());
            var current = (await api.GetFromJsonAsync<RollbackOperationResponse>(
                "/api/v1/messaging/delivery/rollback-operations?eventType=" + selected.EventType + "&schemaVersion=1"))!;
            Assert.AreEqual(operation.Generation, current.Generation); Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, current.Phase);
            Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = selected.EventType }));
            Assert.AreEqual(binding, await database.QuerySingleAsync<string>("SELECT CdcKafkaBindingJson FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = selected.EventType }));
            Assert.AreEqual(resources, await database.QuerySingleAsync<string>("SELECT ResourcesJson FROM fn_messaging_rollback_operation WHERE Id=@id", new { id = operation.Generation }));
            Assert.AreEqual(resumeCount, control.ResumeCount); Assert.IsTrue(await connect.IsConnectorPausedAsync(connector));
            Assert.AreEqual(oldHigh, await GroupOffsetAsync(partition));
            if (replay is not null)
            {
                using var blocked = await api.PostAsJsonAsync($"/api/v1/messaging/kafka/replay-operations/{replay.OperationId}/execute",
                    new ChangeKafkaReplayOperationRequest("expired fixed replay window"));
                Assert.AreEqual(HttpStatusCode.Conflict, blocked.StatusCode, await blocked.Content.ReadAsStringAsync());
                var currentReplay = (await api.GetFromJsonAsync<KafkaReplayOperationResponse>($"/api/v1/messaging/kafka/replay-operations/{replay.OperationId}"))!;
                Assert.AreEqual("blocked", currentReplay.State); Assert.AreEqual("resource_or_window_changed", currentReplay.LastReasonCode);
                Assert.AreEqual(1L, currentReplay.ConfirmedMessages); Assert.AreEqual(replayNext, currentReplay.Partitions.Single().NextOffset);
                Assert.AreEqual(1L, currentReplay.AlreadyProcessedMessages); Assert.AreEqual(0L, currentReplay.ProcessedMessages);
                Assert.IsFalse(currentReplay.HasUnconfirmedMessage);
                Assert.AreEqual(replaySnapshot, await database.QuerySingleAsync<string>("SELECT SnapshotJson FROM fn_messaging_replay_operation WHERE Id=@id", new { id = replay.OperationId }));
                Assert.AreEqual(replayRequest, await database.QuerySingleAsync<string>("SELECT RequestJson FROM fn_messaging_replay_operation WHERE Id=@id", new { id = replay.OperationId }));
            }
            await File.AppendAllTextAsync(Path.Combine(evidenceDirectory, "real-business-stream-ttl.ndjson"),
                JsonSerializer.Serialize(new { Target = target, EventType = selected.EventType, Topic = topic, before.TopicId,
                    OriginalLow = before.Partitions.Single().Low, OldHigh = oldHigh, ExpiredLow = expired.Partitions.Single().Low,
                    OriginalBindingPreserved = true, OriginalResourcesPreserved = true, ProducerFence = 1,
                    ResumeDelta = control.ResumeCount - resumeCount, IndependentRealBusinessStreams = new[] { streams[0].EventType, streams[1].EventType, streams[3].EventType },
                    TargetApprovalStatus = 200, CompletedRelayBlocked = true, SourceCompletionBlocked = true,
                    FixedReplayWindowBlocked = replay is not null,
                    RetryUsesRealEnvelopeCopy = target == "retry" }) + Environment.NewLine);
        }
        finally { gate.Release.TrySetResult(); }

        async Task<long> HighAsync(TopicPartition part)
        {
            var offset = (await broker.ListOffsetsAsync([new TopicPartitionOffsetSpec { TopicPartition = part, OffsetSpec = OffsetSpec.Latest() }])).ResultInfos.Single().TopicPartitionOffsetError;
            Assert.IsFalse(offset.Error.IsError); return offset.Offset.Value;
        }
        async Task<long> GroupOffsetAsync(TopicPartition part)
        {
            var offset = (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group, [part])])).Single().Partitions.Single();
            Assert.IsFalse(offset.Error.IsError, "目标消费组位点必须实际可读。");
            return offset.Offset.Value;
        }
        Task<long> CountOutboxAsync(string type) => database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE MessageType=@type AND SchemaVersion=1", new { type });
        Task<long> CountInboxAsync(string type) => database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_messaging_inbox_message WHERE ConsumerName=@group AND MessageType=@type AND SchemaVersion=1 AND Status='processed'", new { group, type });
    }
}
