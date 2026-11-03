using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.IntegrationTests.Messaging;

public sealed partial class MessagingMultiProcessRecoveryTests
{
    private static int CapacityPartitionBuffer()
    {
        var depth = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_CAPACITY_PARTITION_BUFFER") switch
        {
            null or "" or "1" => 1,
            "8" => 8,
            _ => throw new InvalidOperationException("本机有界预取对照仅允许1或8项。"),
        };
        var duration = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_DUAL_WORKER_SECONDS");
        var rate = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_MESSAGES_PER_SECOND");
        var explicitSoak = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_PREFETCH8_SOAK") == "1";
        if (explicitSoak && (!MessagingTestProcess.NativeEnabled || depth != 8 || rate != "1" || duration is not ("60" or "3600")))
            throw new InvalidOperationException("显式预取8短测/长稳固定Native、8/0/1及每秒一次业务写入。");
        if (depth != 1 && (!MessagingTestProcess.NativeEnabled
            || !(explicitSoak || duration == "60" && rate is "5" or "10")))
            throw new InvalidOperationException("八项预取只能由显式Native容量或预取8短测/长稳入口选择。");
        return depth;
    }

    /// <summary>相同实际 HTTP→事务 Outbox→Debezium→双 Worker→Inbox/业务投影，短测和长跑共用冻结输入。</summary>
    private async Task SoakAsync(DatabaseOptions database, DbConnection connection, CdcDebeziumPipelineEnvironment pipeline,
        IAdminClient broker, HttpClient business, string accessToken, Guid tenantId, MessagingTestProcess workerA,
        MessagingTestProcess workerB, MessagingTestProcess apiA, MessagingTestProcess apiB, Func<string, Task<MessagingTestProcess>> startWorker, string databaseId,
        KafkaMessagingOptions kafka, CdcKafkaDeliveryBinding topology, MessagingIndependentStreamProbe independentProbe)
    {
        var seconds = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_DUAL_WORKER_SECONDS") switch
        {
            "60" => 60,
            "3600" => 3600,
            null or "" => 60,
            _ => throw new InvalidOperationException("双 Worker 验收只能通过正式入口选择 60 秒预检或 3600 秒长跑。"),
        };
        var rate = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_MESSAGES_PER_SECOND") switch
        {
            "5" when MessagingTestProcess.NativeEnabled && seconds == 60 => 5,
            "10" when MessagingTestProcess.NativeEnabled && seconds == 60 => 10,
            null or "" or "1" => 1,
            _ => throw new InvalidOperationException("仅本机Native短时容量测量允许5/10条每秒；长稳固定1条每秒。"),
        };
        var plannedMessages = seconds * rate;
        var partitionBuffer = CapacityPartitionBuffer();
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, database.Provider + "-dual-worker");
        Directory.CreateDirectory(evidence);
        var baselineIds = (await connection.QueryAsync<Guid>("SELECT Id FROM fn_messaging_outbox_event WHERE MessageType=@EventType", new { EventType })).ToArray();
        var ids = new List<Guid>(); var latencies = new List<double>(); var failures = 0; var renewals = 0;
        using var authentication = new HttpClient { BaseAddress = business.BaseAddress };
        authentication.DefaultRequestHeaders.Host = "localhost";
        var memberSamples = new List<int>(); var workerMemory = new List<long>(); var apiMemory = new List<long>();
        using var sampleGate = new SemaphoreSlim(1, 1);
        var brokerCheckpoints = new List<object>();
        var clock = Stopwatch.StartNew();
        MessagingTestProcess? replacement = null;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 240));
        var deadlocksBefore = await DeadlocksAsync();
        var faults = FaultsAsync();
        var resources = new MessagingSoakResourceRecorder([databaseId, .. pipeline.OwnedContainerIds], Path.Combine(evidence, "resources.ndjson"), SampleAsync);
        try
        {
            for (var index = 0; index < plannedMessages; index++)
            {
                if (resources.Completion.IsFaulted) await resources.Completion;
                if (faults.IsFaulted) await faults;
                var delay = TimeSpan.FromSeconds((double)index / rate) - clock.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, lifetime.Token);
                if (index > 0 && index % (Math.Min(300, seconds / 2) * rate) == 0)
                {
                    // 保持真实的默认短期 JWT；通过受保护租户入口续建会话，不为长跑关闭认证或延长令牌。
                    var hostToken = await FullNetApiFactoryLogin.LoginAsHostAdminAsync(authentication, lifetime.Token);
                    var renewed = await FullNetApiFactoryLogin.EnterAcmeTenantAsync(authentication, hostToken, lifetime.Token);
                    Assert.AreEqual(tenantId, renewed.TenantId); accessToken = renewed.AccessToken; renewals++;
                }
                var request = Stopwatch.StartNew();
                try
                {
                    var unit = await FullNetApiFactoryOrganization.CreateUnitAsync(business, accessToken,
                        "dual-soak-" + Guid.NewGuid().ToString("N"), "dual worker " + index, lifetime.Token);
                    ids.Add(unit.Id);
                }
                catch { failures++; throw; }
                finally { latencies.Add(request.Elapsed.TotalMilliseconds); }
            }
            // Windows 计时器可能提前返回不足一毫秒；按单调时钟补足整段测量，不放宽最小时长断言。
            while (true)
            {
                var remaining = TimeSpan.FromSeconds(seconds) - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining, lifetime.Token);
            }
            var measuredSeconds = clock.Elapsed.TotalSeconds;
            await faults;
            Assert.IsNotNull(replacement);
            Assert.AreNotEqual(workerA.Id, replacement.Id);
            await WaitAsync(async () =>
            {
                var projected = 0;
                foreach (var batch in ids.Chunk(1000)) projected += await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_identity_organization_unit_projection WHERE TenantId=@TenantId AND UnitId IN @Ids", new { TenantId = tenantId, Ids = batch });
                return projected == ids.Count;
            },
                "双 Worker 长跑全部业务投影完成");
            await WaitAsync(async () => (await CountsAsync()).Pending == 0, "双 Worker 长跑 Inbox 对账排空");
            await WaitAsync(BrokerDrainedAsync, "双 Worker 长跑正式和重试组位点与真实 Broker 水位对齐");
            await WaitAsync(async () => (await broker.DescribeConsumerGroupsAsync([Group])).ConsumerGroupDescriptions.Single().Members.Count == 2, "双 Worker 长跑最终双成员");
            var drainSeconds = Math.Max(0, clock.Elapsed.TotalSeconds - measuredSeconds);
            // 该探针只验证第二个真实 Connector，不计入业务流的排空或 Worker 吞吐。
            await independentProbe.VerifyAsync("after-worker-and-broker-faults");
            await SampleAsync(lifetime.Token);
            int[] finalMembers; long maximumWorkerMemory; long maximumApiMemory;
            await sampleGate.WaitAsync(lifetime.Token);
            try { finalMembers = memberSamples.ToArray(); maximumWorkerMemory = workerMemory.Max(); maximumApiMemory = apiMemory.Max(); }
            finally { sampleGate.Release(); }
            var counts = await CountsAsync();
            Assert.AreEqual(plannedMessages, ids.Count); Assert.AreEqual(0, failures);
            Assert.IsLessThanOrEqualTo(rate > 1 ? seconds / .95 : seconds * 1.25, measuredSeconds, "有界容量档实际吞吐必须达到目标的95%。");
            Assert.IsGreaterThanOrEqualTo((double)seconds, measuredSeconds);
            Assert.AreEqual((long)plannedMessages, counts.Produced); Assert.AreEqual((long)plannedMessages, counts.Processed);
            Assert.AreEqual(0L, counts.Pending);
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_dead_letter WHERE ConsumerName=@Group", new { Group }));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM (SELECT MessageId FROM fn_messaging_inbox_message WHERE ConsumerName=@Group GROUP BY MessageId HAVING COUNT(*)<>1) duplicates", new { Group }));
            Assert.AreEqual(2, finalMembers[^1]);
            var deadlocksAfter = await DeadlocksAsync();
            Assert.AreEqual(deadlocksBefore, deadlocksAfter, "双 Worker 长跑不得新增数据库死锁。");
            var latencySql = database.Provider == DatabaseProvider.SqlServer
                ? "CONVERT(float,DATEDIFF_BIG(MICROSECOND,sample.OccurredAtUtc,inbox.ProcessedAtUtc))/1000.0"
                : "TIMESTAMPDIFF(MICROSECOND,sample.OccurredAtUtc,inbox.ProcessedAtUtc)/1000.0";
            var deliveryLatencies = (await connection.QueryAsync<double>($"""
                SELECT {latencySql} FROM fn_messaging_outbox_event sample
                INNER JOIN fn_messaging_inbox_message inbox ON inbox.MessageId=sample.Id AND inbox.ConsumerName=@Group AND inbox.Status='processed'
                WHERE sample.MessageType=@EventType AND sample.Id NOT IN @BaselineIds
                """, new { Group, EventType, BaselineIds = baselineIds })).Order().ToArray();
            Assert.AreEqual(plannedMessages, deliveryLatencies.Length);
            Assert.IsTrue(deliveryLatencies.All(value => double.IsFinite(value) && value >= 0));
            if (partitionBuffer > 1)
            {
                // 追加核对按源事件排序的 Inbox 处理时间；该时间列不等于 Kafka 位点或数据库提交时间。
                Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*) FROM (
                        SELECT inbox.ProcessedAtUtc,
                            LAG(inbox.ProcessedAtUtc) OVER (ORDER BY sample.OccurredAtUtc, sample.Id) AS PreviousProcessedAtUtc
                        FROM fn_messaging_outbox_event sample
                        INNER JOIN fn_messaging_inbox_message inbox ON inbox.MessageId=sample.Id AND inbox.ConsumerName=@Group AND inbox.Status='processed'
                        WHERE sample.MessageType=@EventType AND sample.Id NOT IN @BaselineIds
                    ) ordered_events WHERE ProcessedAtUtc<PreviousProcessedAtUtc
                    """, new { Group, EventType, BaselineIds = baselineIds }), "按源事件排序的 Inbox 处理时间不得倒退。");
            }
            latencies.Sort();
            if (MessagingTestProcess.NativeEnabled)
            {
                await resources.DisposeAsync();
                await workerB.StopGracefullyAsync(); await replacement.StopGracefullyAsync();
            }
            await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new
            {
                Provider = database.Provider.ToString(), PlannedSeconds = seconds, MeasuredSeconds = measuredSeconds,
                MessagesPerSecond = rate, PlannedMessages = plannedMessages, AchievedRequestsPerSecond = ids.Count / measuredSeconds,
                PartitionBufferHighWatermark = partitionBuffer, PartitionBufferLowWatermark = 0,
                PartitionKeyConcurrencySlots = 1, OffsetCommitMode = "PerMessage", ProxyNoDelay = true, ProxySingleWrite = true,
                Prefetch8SoakProfile = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_PREFETCH8_SOAK") == "1",
                InboxProcessedTimeNondecreasingVerified = partitionBuffer > 1,
                SyntheticIndependentStreamVerified = true,
                ActiveBusinessStreamCount = 1,
                Attempted = latencies.Count, Succeeded = ids.Count, Failures = failures,
                AuthenticationRenewals = renewals,
                P50Milliseconds = Percentile(.50), P95Milliseconds = Percentile(.95), P99Milliseconds = Percentile(.99),
                DeliveryP50Milliseconds = DeliveryPercentile(.50), DeliveryP95Milliseconds = DeliveryPercentile(.95), DeliveryP99Milliseconds = DeliveryPercentile(.99),
                Produced = counts.Produced, Processed = counts.Processed, Pending = counts.Pending,
                BrokerDrainVerified = true, BrokerCheckpoints = brokerCheckpoints,
                DrainSeconds = drainSeconds,
                ExecutionKind = MessagingTestProcess.NativeEnabled ? "native_linux" : "jit",
                WorkerResourceKind = MessagingTestProcess.NativeEnabled ? "linux_pid1_rss" : "windows_process_handle",
                NativeWorkerContainers = new[] { workerA.NativeContainerName, workerB.NativeContainerName, replacement.NativeContainerName },
                WorkerPids = MessagingTestProcess.NativeEnabled ? Array.Empty<int>() : new[] { workerA.Id, workerB.Id, replacement.Id }, MemberSamples = finalMembers,
                MaximumWorkerWorkingSetBytes = maximumWorkerMemory,
                MaximumApiWorkingSetBytes = maximumApiMemory,
                DatabaseDeadlocksBefore = deadlocksBefore, DatabaseDeadlocksAfter = deadlocksAfter,
                Scope = (MessagingTestProcess.NativeEnabled ? "Native Linux; " : "JIT; ") + $"Local {rate} HTTP requests/second, one business stream and an independent synthetic CDC filter probe; actual API, two Workers, broker interruption and Worker replacement; Capacity-not-verified",
            }, new JsonSerializerOptions { WriteIndented = true }));
            Step(database, $"dual Worker {(seconds == 3600 ? "60 minute soak" : "short preflight")} passed: {ids.Count} messages, zero pending");
            double DeliveryPercentile(double percentile) => deliveryLatencies[(int)Math.Ceiling(percentile * deliveryLatencies.Length) - 1];
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await faults; } catch (OperationCanceledException) { }
            finally
            {
                try { await resources.DisposeAsync(); }
                finally { if (replacement is not null) await replacement.DisposeAsync(); }
            }
        }

        double Percentile(double percentile) => latencies[(int)Math.Ceiling(percentile * latencies.Count) - 1];
        async Task<long> DeadlocksAsync()
        {
            if (database.Provider == DatabaseProvider.SqlServer)
                return await connection.QuerySingleAsync<long>("SELECT cntr_value FROM sys.dm_os_performance_counters WHERE counter_name='Number of Deadlocks/sec' AND instance_name='_Total' AND object_name LIKE '%:Locks%'");
            // MySQL 8.0 的死锁计数来自已启用的 InnoDB 指标；不存在或被关闭时必须失败，不能填零。
            return await connection.QuerySingleAsync<long>("SELECT `COUNT` FROM information_schema.INNODB_METRICS WHERE NAME='lock_deadlocks' AND STATUS='enabled'");
        }
        async Task<bool> BrokerDrainedAsync()
        {
            var observer = new KafkaConsumerLagObserver();
            var current = await observer.ObserveTopicBindingsAsync(kafka, topology.Topics.Select(topic => topic.TopicName).ToArray(), lifetime.Token);
            Assert.IsTrue(KafkaDeliveryTopology.Matches(topology.Topics, current), "长跑不能裁剪或重建原正式/重试 Topic 来伪造排空。");
            var checkpoints = new List<object>();
            foreach (var group in topology.ConsumerGroups)
                foreach (var topic in current!)
                {
                    if (!await observer.IsDrainedAsync(kafka, topic, group, lifetime.Token)) return false;
                    var offsets = (await broker.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group,
                        topic.Partitions.Select(partition => new TopicPartition(topic.TopicName, partition.Partition)).ToList())])).Single().Partitions;
                    foreach (var partition in topic.Partitions)
                    {
                        var committed = offsets.Single(offset => offset.Partition.Value == partition.Partition).Offset.Value;
                        // 从未写入的空分区可没有提交；有过数据的分区必须精确确认到 High，不能以 Inbox 数量代替 Broker 确认。
                        if (committed < 0 ? partition.Low != 0 || partition.High != 0 : committed != partition.High) return false;
                        checkpoints.Add(new { ConsumerGroup = group, Topic = topic.TopicName, partition.Partition, partition.Low, partition.High, Committed = committed });
                    }
                }
            brokerCheckpoints = checkpoints;
            return true;
        }
        async Task FaultsAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds / 4), lifetime.Token);
            await workerA.CrashAsync();
            replacement = await startWorker("soak-replacement");
            await File.AppendAllTextAsync(Path.Combine(evidence, "faults.ndjson"), JsonSerializer.Serialize(new
            {
                AtUtc = DateTimeOffset.UtcNow, Fault = "worker_crash_and_replacement",
                OldPid = MessagingTestProcess.NativeEnabled ? (int?)null : workerA.Id,
                NewPid = MessagingTestProcess.NativeEnabled ? (int?)null : replacement.Id,
                OldDockerCliPid = MessagingTestProcess.NativeEnabled ? (int?)workerA.Id : null,
                NewDockerCliPid = MessagingTestProcess.NativeEnabled ? (int?)replacement.Id : null,
                OldNativeContainer = workerA.NativeContainerName, NewNativeContainer = replacement.NativeContainerName,
            }) + Environment.NewLine);
            var delay = TimeSpan.FromSeconds(seconds / 2) - clock.Elapsed;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, lifetime.Token);
            await pipeline.InterruptBrokerAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            await File.AppendAllTextAsync(Path.Combine(evidence, "faults.ndjson"), JsonSerializer.Serialize(new { AtUtc = DateTimeOffset.UtcNow, Fault = "owned_broker_pause_10_seconds_recovered" }) + Environment.NewLine);
        }
        async Task<(long Produced, long Processed, long Pending, double OldestPendingMilliseconds)> CountsAsync()
        {
            // 只对本次测量开始后的正式事件对账；不把 Legacy/切流探针计入长跑吞吐。
            await using DbConnection sample = database.Provider == DatabaseProvider.SqlServer
                ? new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString)
                : new MySqlConnector.MySqlConnection(Full.NET.Data.MySql.MySqlConnectionStringPolicy.Create(database.ConnectionString, database.MySqlGuidStorageMode, false));
            var oldest = database.Provider == DatabaseProvider.SqlServer
                ? "COALESCE(CONVERT(float,DATEDIFF_BIG(MILLISECOND,MIN(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN sample.OccurredAtUtc END),SYSUTCDATETIME())),0)"
                : "COALESCE(TIMESTAMPDIFF(MICROSECOND,MIN(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN sample.OccurredAtUtc END),UTC_TIMESTAMP(6))/1000.0,0)";
            return await sample.QuerySingleAsync<(long, long, long, double)>($"""
                SELECT COUNT(*), COALESCE(SUM(CASE WHEN inbox.Status='processed' THEN 1 ELSE 0 END),0),
                    COALESCE(SUM(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN 1 ELSE 0 END),0), {oldest}
                FROM fn_messaging_outbox_event sample
                LEFT JOIN fn_messaging_inbox_message inbox ON inbox.MessageId=sample.Id AND inbox.ConsumerName=@Group
                WHERE sample.MessageType=@EventType AND sample.Id NOT IN @BaselineIds
                """, new { EventType, Group, BaselineIds = baselineIds });
        }
        async Task SampleAsync(CancellationToken token)
        {
            // 周期采样和最终采样共用同一文件及集合，串行化以防写句柄冲突或枚举时集合变化。
            await sampleGate.WaitAsync(token);
            try
            {
                var counts = await CountsAsync();
                int? members;
                try { members = (await broker.DescribeConsumerGroupsAsync([Group], new() { RequestTimeout = TimeSpan.FromSeconds(5) })).ConsumerGroupDescriptions.Single().Members.Count; }
                catch (KafkaException) { members = null; }
                if (members is { } value) memberSamples.Add(value);
                var processes = (await Task.WhenAll(new[] { workerA, workerB, replacement }.Where(worker => worker is not null)
                    .Select(worker => worker!.ReadResourcesAsync(token)))).Where(sample => sample is not null).Select(sample => sample!).ToArray();
                var apis = (await Task.WhenAll(new[] { apiA, apiB }.Select(api => api.ReadResourcesAsync(token))))
                    .Where(sample => sample is not null).Select(sample => sample!).ToArray();
                if (clock.Elapsed.TotalSeconds >= seconds) Assert.AreEqual(2, processes.Length, "最终资源采样必须来自两个存活的真实Worker。");
                Assert.AreEqual(2, apis.Length, "两个API必须持续存活并提供实际进程采样。");
                foreach (var process in processes) workerMemory.Add(process.WorkingSetBytes);
                foreach (var api in apis) apiMemory.Add(api.WorkingSetBytes);
                await File.AppendAllTextAsync(Path.Combine(evidence, "backlog.ndjson"), JsonSerializer.Serialize(new
                { AtUtc = DateTimeOffset.UtcNow, ElapsedSeconds = clock.Elapsed.TotalSeconds, counts.Produced, counts.Processed, counts.Pending,
                    counts.OldestPendingMilliseconds, Members = members, Workers = processes, Apis = apis }) + Environment.NewLine, token);
            }
            finally { sampleGate.Release(); }
        }
    }
}
