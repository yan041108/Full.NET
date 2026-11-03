using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>覆盖控制面动作顺序、跨进程资源恢复及失败关闭。</summary>
[TestClass]
public sealed class KafkaDeliveryControlTests
{
    [TestMethod]
    public void Sql_server_same_commit_lsn_does_not_prove_complete_transaction()
    {
        var fence = CdcDeliveryPosition.ForSqlServer(null, "00000027:00000123:0001");
        Assert.IsFalse(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, CdcDeliveryPosition.ForSqlServer(null, "00000027:00000123:0001")));
        Assert.IsTrue(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, CdcDeliveryPosition.ForSqlServer(null, "00000027:00000123:0002")));
    }

    [TestMethod]
    public async Task Reconciliation_round_processes_ready_stream_before_later_timeout_and_rotates()
    {
        KafkaConnectRollbackStreamBinding[] streams = [new() { EventType = "ready" }, new() { EventType = "slow" }];
        var processed = new List<string>();
        async Task RunAsync(int first)
        {
            using var deadline = new CancellationTokenSource();
            await KafkaDeliveryControlReconciler.RunRoundAsync(streams, first, (stream, token) =>
            {
                if (stream.EventType == "slow") { deadline.Cancel(); token.ThrowIfCancellationRequested(); }
                processed.Add(stream.EventType);
                return Task.CompletedTask;
            }, _ => { }, deadline.Token, CancellationToken.None);
        }
        await RunAsync(1);
        Assert.AreEqual(0, processed.Count);
        await RunAsync(0);
        CollectionAssert.AreEqual(new[] { "ready" }, processed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Same_name_topic_recreation_before_prepare_rejects_the_cutover_baseline(bool retry)
    {
        await using var fixture = new ControlFixture();
        fixture.RecreatedTopicName = retry ? KafkaTopicNames.GetRetryTopic("messaging.orders.v1", "5s") : "messaging.orders.v1";
        Assert.AreEqual(EventDeliveryRollbackReadiness.Unavailable,
            await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Same_name_topic_recreation_after_pause_cannot_complete_or_resume(bool retry)
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Paused = true;
            fixture.RecreatedTopicName = retry ? KafkaTopicNames.GetRetryTopic("messaging.orders.v1", "5s") : "messaging.orders.v1";
            return Task.CompletedTask;
        });
        Assert.AreEqual(EventDeliveryRollbackReadiness.Unavailable,
            await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Recovering };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Removing_a_retry_stage_since_cutover_cannot_ignore_its_old_topic()
    {
        await using var fixture = new ControlFixture();
        Assert.AreEqual(EventDeliveryRollbackReadiness.Unavailable,
            await fixture.CreateReader(retryStages: []).PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }
    [TestMethod]
    public async Task Prepare_waits_for_cdc_before_pause_and_drains_every_group_and_retry_topic()
    {
        await using var fixture = new ControlFixture();
        var positions = 0;
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult<CdcDeliveryPosition?>(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", ++positions == 1 ? 1 : 10)));
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(positions >= 2, "CDC 尚未覆盖边界时不得暂停。");
            Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, fixture.Journal.Operation.Phase);
            Assert.IsNotNull(fixture.Journal.Operation.ResourcesJson);
            fixture.Paused = true;
            return Task.CompletedTask;
        });
        var proof = await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        Assert.IsTrue(proof.ConnectorStopped && proof.BrokerMessagesDrainedOrIsolated && proof.SourcePositionCoversProducerFence);
        CollectionAssert.AreEquivalent(new[]
        {
            "fullnet.messaging.orders.a|messaging.orders.v1", "fullnet.messaging.orders.b|messaging.orders.v1",
            "fullnet.messaging.orders.a|messaging.orders.v1.retry.5s", "fullnet.messaging.orders.b|messaging.orders.v1.retry.5s",
        }, fixture.Queries.Distinct().ToArray());
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Sql_server_same_commit_proof_is_rechecked_after_pause(bool stillCovered)
    {
        await using var fixture = new ControlFixture();
        const string commit = "00000027:00000123:0001";
        var fence = CdcDeliveryPosition.ForSqlServer(null, commit);
        var connector = CdcDeliveryPosition.ForSqlServer(null, commit, "00000027:00000120:0001", 1);
        fixture.Fence.TryReadAsync(ControlFixture.EventType, 1, fixture.Generation, Arg.Any<CancellationToken>())
            .Returns(new EventDeliveryProducerFenceSnapshot(fixture.Generation, fence, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        fixture.Fence.IsSameTransactionCoveredAsync(ControlFixture.EventType, 1, fixture.Generation, fence, connector, Arg.Any<CancellationToken>())
            .Returns(true, stillCovered);
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(connector);
        var proof = await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        Assert.AreEqual(stillCovered, proof.ConnectorStopped);
        Assert.IsTrue(fixture.Paused, "初次证明通过后必须实际暂停，之后重新检查。");
        await fixture.Fence.Received(2).IsSameTransactionCoveredAsync(ControlFixture.EventType, 1, fixture.Generation,
            fence, connector, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Pause_response_loss_is_recoverable_by_a_new_reader_using_persisted_resources()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Paused = true;
            return Task.FromException(new HttpRequestException("暂停已生效但响应丢失"));
        });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, fixture.Journal.Operation.Phase);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Recovering);
        fixture.Options.Streams[0].ConnectorName = "new-config-must-not-be-used";
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
        await fixture.Connect.DidNotReceive().ResumeConnectorAsync("new-config-must-not-be-used", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Resume_http_success_without_running_tasks_does_not_report_recovery()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Connect.WaitForConnectorHealthyAsync("orders", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(null)]
    public async Task Lost_or_unknown_source_window_cannot_resume_a_running_connector(bool? recoverable)
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Source.ReadSourcePositionAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>())
            .Returns(new CdcSourcePositionHealthSnapshot(recoverable, null));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceive().ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    public async Task Source_window_lost_while_resuming_cannot_report_recovery()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Connect.ResumeConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Source.ReadSourcePositionAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>())
                .Returns(new CdcSourcePositionHealthSnapshot(false, null));
            return Task.CompletedTask;
        });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    public async Task Completed_generation_cannot_resume_the_old_connector()
    {
        await using var fixture = new ControlFixture();
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Completed };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Changed_control_plane_address_keeps_recovery_closed()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        fixture.Options.ConnectBaseUri = "http://another-connect:8083";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Shared_connector_mapping_is_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        fixture.Options.Streams = [fixture.Options.Streams[0], new()
        {
            EventType = "fullnet.messaging.other.changed", SchemaVersion = 1, ConnectorName = "orders",
            TopicName = "messaging.other.v1", ConsumerGroupId = "fullnet.messaging.other",
        }];
        var proof = await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        Assert.IsFalse(proof.ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Connector_configuration_change_prevents_resuming_a_different_resource()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        var changed = ControlFixture.ConnectorConfiguration();
        changed["database.hostname"] = "another-database-server";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(changed);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Failure_before_pause_confirms_running_without_sending_resume()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask), cancellation.Token));
        Assert.AreEqual(EventDeliveryRollbackPhase.Prepared, fixture.Journal.Operation.Phase);
        Assert.IsFalse(fixture.Journal.Operation.HasPauseIntent);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.Recovering);
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Recovery_before_pause_waits_for_first_real_committed_position()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns((CdcDeliveryPosition?)null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask), cancellation.Token));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.Recovering);
        var reads = 0;
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult<CdcDeliveryPosition?>(++reads < 3 ? null : CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10)));
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        Assert.IsTrue(reads >= 4);
        await fixture.Source.Received(2).ReadSourcePositionAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>());
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Recovery_before_pause_without_position_keeps_fence_on_cancellation()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns((CdcDeliveryPosition?)null);
        using var preparation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask), preparation.Token));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.Recovering);
        using var recovery = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask), recovery.Token));
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
        await fixture.Source.DidNotReceiveWithAnyArgs().ReadSourcePositionAsync(default!, default!, default);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Recovery_rejects_connector_replacement_while_waiting_for_first_position()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns((CdcDeliveryPosition?)null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask), cancellation.Token));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Prepared, EventDeliveryRollbackPhase.Recovering);
        var reads = 0;
        fixture.Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++reads == 2)
            {
                var changed = ControlFixture.ConnectorConfiguration();
                changed["database.hostname"] = "replacement";
                fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(changed);
            }
            return Task.FromResult<CdcDeliveryPosition?>(reads < 2 ? null : CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10));
        });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask)));
        Assert.AreEqual(EventDeliveryRollbackPhase.Recovering, fixture.Journal.Operation.Phase);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Other_default_cdc_owner_is_rejected_even_without_a_persisted_row()
    {
        await using var fixture = new ControlFixture(includeOtherDefaultCdc: true);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask))).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Committed_offset_ahead_of_current_log_end_is_not_a_drain_proof()
    {
        var partition = new TopicPartition("topic", 0);
        var query = Substitute.For<IKafkaLagQueryClient>();
        query.ReadPartitionsAsync("topic").Returns(new[] { partition });
        query.ReadCommittedOffsetsAsync("group", Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long> { [partition] = 2 });
        query.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(new Dictionary<TopicPartition, long> { [partition] = 1 });
        Assert.IsNull(await new KafkaConsumerLagObserver(_ => query).ObserveLagAsync(new(), "topic", "group", CancellationToken.None));
    }

    [TestMethod]
    public async Task Real_http_client_preserves_pause_intent_and_requires_running_tasks_after_resume()
    {
        await using var fixture = new ControlFixture();
        using var handler = new ControlHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://connect:8083") };
        using var connect = new KafkaConnectAdminClient(http);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            fixture.CreateReader(connect).PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        Assert.IsTrue(handler.Paused);
        Assert.IsTrue(fixture.Journal.Operation.HasPauseIntent);
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.PauseRequested, EventDeliveryRollbackPhase.Recovering);
        handler.FailResumeTasks = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateReader(connect).AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask)));
        handler.FailResumeTasks = false;
        await fixture.CreateReader(connect).AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        Assert.AreEqual(2, handler.ResumeCalls);
    }

    [TestMethod]
    public async Task Different_connect_and_broker_clusters_are_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.TryReadKafkaClusterIdAsync(Arg.Any<CancellationToken>()).Returns("another-cluster");
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask))).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Broker_cluster_change_during_drain_cannot_create_final_proof()
    {
        await using var fixture = new ControlFixture();
        fixture.Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.Paused = true;
            fixture.BrokerClusterId = "changed-during-drain";
            return Task.CompletedTask;
        });
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask))).ConnectorStopped);
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
        Assert.IsTrue(fixture.Journal.Operation.HasPauseIntent);
    }

    [TestMethod]
    public async Task Credential_rotation_does_not_change_resource_identity_or_store_credentials()
    {
        await using var fixture = new ControlFixture();
        await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        Assert.IsFalse(fixture.Journal.Operation.ResourcesJson!.Contains("credential-v1", StringComparison.Ordinal));
        await fixture.Journal.TransitionAsync(fixture.Generation, EventDeliveryRollbackPhase.Paused, EventDeliveryRollbackPhase.Recovering);
        var rotated = ControlFixture.ConnectorConfiguration();
        rotated["database.password"] = "credential-v2";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(rotated);
        await fixture.CreateReader().AbortAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask));
        await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Wrong_source_database_is_rejected_before_pause()
    {
        await using var fixture = new ControlFixture();
        var wrong = ControlFixture.ConnectorConfiguration();
        wrong["database.include.list"] = "another-database";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(wrong);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask))).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Json_payload_converter_cannot_replace_the_required_raw_event_transport()
    {
        await using var fixture = new ControlFixture();
        var configuration = ControlFixture.ConnectorConfiguration();
        configuration["value.converter"] = "org.apache.kafka.connect.json.JsonConverter";
        fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(configuration);
        Assert.IsFalse((await fixture.CreateReader().PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask))).ConnectorStopped);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Unknown_retry_lag_cannot_be_used_as_a_drain_proof()
    {
        await using var fixture = new ControlFixture();
        fixture.UnknownRetry = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Assert.AreEqual(EventDeliveryRollbackReadiness.Unavailable, await fixture.CreateReader()
            .PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new EventDeliveryControlLease(_ => Task.CompletedTask), timeout.Token));
        Assert.AreEqual(EventDeliveryRollbackPhase.Paused, fixture.Journal.Operation.Phase);
        Assert.IsNotNull(fixture.Journal.Operation.ResourcesJson);
    }

    [TestMethod]
    public void Api_control_registration_does_not_register_hosted_consumers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:KafkaConnectRollback:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddFullNetKafkaDeliveryControl(configuration);
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(IHostedService)));
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(IEventDeliveryRollbackReadinessReader)
            && item.ImplementationFactory is not null));
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(KafkaConnectEventDeliveryRollbackReadinessReader)));
    }

    [TestMethod]
    public void Enabled_control_options_reject_duplicate_streams_and_unbounded_waits()
    {
        var validator = new KafkaConnectRollbackOptionsValidator();
        var options = ControlFixture.CreateOptions();
        Assert.IsTrue(validator.Validate(null, options).Succeeded);
        options.Streams = [options.Streams[0], options.Streams[0]];
        Assert.IsTrue(validator.Validate(null, options).Failed);
        options = ControlFixture.CreateOptions();
        options.PrepareTimeoutSeconds = int.MaxValue;
        Assert.IsTrue(validator.Validate(null, options).Failed);
    }

    [TestMethod]
    public async Task Lost_control_lease_cannot_send_pause()
    {
        await using var fixture = new ControlFixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().PrepareAsync(
            ControlFixture.EventType, 1, fixture.Generation, new(_ => throw new InvalidOperationException("lost lease"))));
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        Assert.AreEqual(EventDeliveryRollbackPhase.Prepared, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    public async Task Lost_control_lease_after_pause_cannot_claim_paused()
    {
        await using var fixture = new ControlFixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.CreateReader().PrepareAsync(
            ControlFixture.EventType, 1, fixture.Generation, new(_ => fixture.Paused ? throw new InvalidOperationException("lost lease") : Task.CompletedTask)));
        Assert.AreEqual(EventDeliveryRollbackPhase.PauseRequested, fixture.Journal.Operation.Phase);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Current_terminal_corrects_physical_state_without_changing_journal(bool completed)
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        await reader.PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = completed ? EventDeliveryRollbackPhase.Completed : EventDeliveryRollbackPhase.Recovered };
        fixture.Owner = fixture.Owner with { CurrentOwner = completed ? EventDeliveryOwner.LegacyPolling : EventDeliveryOwner.CdcKafka };
        fixture.Paused = !completed;
        var before = fixture.Journal.Operation;
        Assert.IsTrue(await reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual(completed, fixture.Paused);
        Assert.AreEqual(before, fixture.Journal.Operation);
    }

    [TestMethod]
    [DataRow("fence")]
    [DataRow("later_activation")]
    [DataRow("configuration")]
    [DataRow("topic")]
    [DataRow("window")]
    [DataRow("cluster")]
    [DataRow("source")]
    [DataRow("lease")]
    public async Task Reconciliation_does_not_control_unsafe_or_unowned_resources(string fault)
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        await reader.PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Recovered };
        fixture.Connect.ClearReceivedCalls();
        if (fault == "fence") fixture.RollbackActive = true;
        if (fault == "later_activation")
        {
            fixture.Owner = fixture.Owner with { CurrentOwner = EventDeliveryOwner.LegacyPolling };
            fixture.Journal.CurrentTerminalEnabled = false;
        }
        if (fault == "configuration") fixture.Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string>());
        if (fault == "topic") fixture.RecreatedTopicName = "messaging.orders.v1";
        if (fault == "window") fixture.WindowChanged = true;
        if (fault == "cluster") fixture.BrokerClusterId = "replacement-cluster";
        if (fault == "source") fixture.Source.ReadSourcePositionAsync(Arg.Any<string>(), Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>()).Returns(new CdcSourcePositionHealthSnapshot(false, null));
        var lease = new EventDeliveryControlLease(_ => fault == "lease" ? throw new InvalidOperationException("lost") : Task.CompletedTask);
        if (fault is "fence" or "later_activation") Assert.IsFalse(await reader.ReconcileAsync(ControlFixture.EventType, 1, lease, default));
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, lease, default));
            if (fault != "lease")
            {
                Assert.IsInstanceOfType<KafkaControlReconciliationException>(error);
                var expected = fault == "configuration" ? "connector_configuration_changed"
                    : fault == "source" ? "source_not_recoverable" : "kafka_identity_or_window_changed";
                Assert.AreEqual(expected, ((KafkaControlReconciliationException)error).ReasonCode);
            }
        }
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task New_control_generation_between_observation_and_send_prevents_correction()
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        await reader.PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Recovered };
        fixture.Connect.ClearReceivedCalls();
        Assert.IsFalse(await reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => { fixture.RollbackActive = true; return Task.CompletedTask; }), default));
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task New_cdc_activation_reconciles_its_own_target_without_claiming_old_terminal()
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        await reader.PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Completed };
        var oldOperation = fixture.Journal.Operation;
        fixture.Journal.CurrentTerminalEnabled = false;
        Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(fixture.Owner.CdcKafkaBindingJson, out var baseline));
        fixture.Owner = fixture.Owner with { CdcKafkaBindingJson = (baseline! with { ActivationId = Guid.CreateVersion7() }).ToJson() };
        Assert.IsTrue(await reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.IsFalse(fixture.Paused);
        Assert.AreEqual(oldOperation, fixture.Journal.Operation);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Prepare_rejects_another_control_endpoint_or_environment(bool environmentChanged)
    {
        await using var fixture = new ControlFixture();
        if (environmentChanged)
        {
            Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(fixture.Owner.CdcKafkaBindingJson, out var baseline));
            fixture.Owner = fixture.Owner with { CdcKafkaBindingJson = (baseline! with { EnvironmentName = "Other" }).ToJson() };
        }
        else fixture.Options.ConnectBaseUri = "http://another-connect:8083";
        Assert.AreEqual(EventDeliveryRollbackReadiness.Unavailable, await fixture.CreateReader().PrepareAsync(
            ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask)));
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Legacy_terminal_cannot_claim_another_activation_endpoint_or_environment(bool environmentChanged)
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        await reader.PrepareAsync(ControlFixture.EventType, 1, fixture.Generation, new(_ => Task.CompletedTask));
        fixture.Journal.Operation = fixture.Journal.Operation with { Phase = EventDeliveryRollbackPhase.Completed };
        Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(fixture.Owner.CdcKafkaBindingJson, out var baseline));
        fixture.Owner = fixture.Owner with { CurrentOwner = EventDeliveryOwner.LegacyPolling,
            CdcKafkaBindingJson = (environmentChanged ? baseline! with { EnvironmentName = "Other" } : baseline! with { ConnectBaseUri = "http://another-connect:8083/" }).ToJson() };
        fixture.Paused = false; fixture.Connect.ClearReceivedCalls();
        var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual("resource_mapping_changed", error.ReasonCode);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Healthy_connector_does_not_request_the_control_lock()
    {
        await using var fixture = new ControlFixture();
        var reader = fixture.CreateReader();
        Assert.IsFalse(await reader.NeedsReconciliationAsync(ControlFixture.EventType, 1, default));
        fixture.Paused = true;
        Assert.IsTrue(await reader.NeedsReconciliationAsync(ControlFixture.EventType, 1, default));
        fixture.RollbackActive = true;
        Assert.IsFalse(await reader.NeedsReconciliationAsync(ControlFixture.EventType, 1, default));
    }

    internal static KafkaConnectRollbackOptions MonitoringOptions() => ControlFixture.CreateOptions();
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Multi_stream_terminal_corrects_only_its_isolated_connector(bool completed)
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(completed);
        var target = fixture.Owner; var other = fixture.OtherOwners.Single(); var journal = fixture.Journal.Operation;
        Assert.IsTrue(await reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual(completed, fixture.Paused);
        Assert.AreEqual(target, fixture.Owner); Assert.AreEqual(other, fixture.OtherOwners.Single()); Assert.AreEqual(journal, fixture.Journal.Operation);
        if (completed)
        {
            await fixture.Connect.Received(1).PauseConnectorAsync("orders", Arg.Any<CancellationToken>());
            await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
        }
        else
        {
            await fixture.Connect.Received(1).ResumeConnectorAsync("orders", Arg.Any<CancellationToken>());
            await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        }
    }

    [TestMethod]
    public async Task Multi_stream_retired_legacy_without_binding_fails_closed()
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(false);
        fixture.OtherOwners[0] = fixture.OtherOwners[0] with { CurrentOwner = EventDeliveryOwner.LegacyPolling,
            PreviousOwner = EventDeliveryOwner.CdcKafka, CdcKafkaBindingJson = null };
        var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual("shared_connector", error.ReasonCode);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task Multi_stream_retired_legacy_binding_cannot_share_a_connector(bool completed, bool shared)
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(completed);
        fixture.OtherOwners[0] = fixture.OtherOwners[0] with { CurrentOwner = EventDeliveryOwner.LegacyPolling };
        if (shared) fixture.ChangeOther("connector");
        if (shared)
        {
            var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
            Assert.AreEqual("shared_connector", error.ReasonCode);
            await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
            await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
        }
        else Assert.IsTrue(await reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
    }

    [TestMethod]
    [DataRow("connector", false)] [DataRow("connector", true)]
    [DataRow("shadow_connector", false)] [DataRow("shadow_connector", true)]
    [DataRow("topic", false)] [DataRow("topic", true)]
    [DataRow("topic_id", false)] [DataRow("topic_id", true)]
    [DataRow("shadow_topic", false)] [DataRow("shadow_topic", true)]
    [DataRow("missing_shadow", false)] [DataRow("missing_shadow", true)]
    [DataRow("endpoint", false)] [DataRow("endpoint", true)]
    [DataRow("environment", false)] [DataRow("environment", true)]
    public async Task Multi_stream_rejects_shared_or_incomplete_activations(string fault, bool completed)
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(completed);
        fixture.ChangeOther(fault);
        var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual("shared_connector", error.ReasonCode);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Multi_stream_rechecks_other_activation_after_physical_observation(bool completed)
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(completed);
        var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1,
            new(_ => { fixture.ChangeOther("connector"); return Task.CompletedTask; }), default));
        Assert.AreEqual("shared_connector", error.ReasonCode);
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    public async Task Multi_stream_rejects_effective_cdc_without_persisted_activation()
    {
        await using var fixture = new ControlFixture(includeOtherDefaultCdc: true);
        var reader = await fixture.MultiStreamTerminalAsync(false);
        fixture.OtherOwners.Clear();
        var error = await Assert.ThrowsExactlyAsync<KafkaControlReconciliationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1, new(_ => Task.CompletedTask), default));
        Assert.AreEqual("shared_connector", error.ReasonCode);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Multi_stream_rechecks_live_lease_immediately_before_send(bool completed)
    {
        await using var fixture = new ControlFixture();
        var reader = await fixture.MultiStreamTerminalAsync(completed); var checks = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReconcileAsync(ControlFixture.EventType, 1,
            new(_ => ++checks == 2 ? throw new InvalidOperationException("lost") : Task.CompletedTask), default));
        await fixture.Connect.DidNotReceiveWithAnyArgs().PauseConnectorAsync(default!, default);
        await fixture.Connect.DidNotReceiveWithAnyArgs().ResumeConnectorAsync(default!, default);
    }

    internal static Dictionary<string, string> MonitoringConfiguration() => ControlFixture.ConnectorConfiguration();

    private sealed class ControlFixture : IAsyncDisposable
    {
        internal IEventDeliveryProducerFencePositionReader Fence { get; } = Substitute.For<IEventDeliveryProducerFencePositionReader>();
        internal const string EventType = "fullnet.messaging.order.changed";
        internal Guid Generation { get; } = Guid.CreateVersion7();
        internal MemoryJournal Journal { get; }
        internal IKafkaConnectAdminClient Connect { get; } = Substitute.For<IKafkaConnectAdminClient>();
        internal ICdcPlatformHealthReader Source { get; } = Substitute.For<ICdcPlatformHealthReader>();
        internal KafkaConnectRollbackOptions Options { get; } = CreateOptions();
        internal List<string> Queries { get; } = [];
        internal bool Paused { get; set; }
        internal bool UnknownRetry { get; set; }
        internal string? RecreatedTopicName { get; set; }
        internal string BrokerClusterId { get; set; } = "test-cluster";
        internal EventStreamOwnershipRecord Owner { get; set; } = null!;
        internal bool WindowChanged { get; set; }
        internal bool RollbackActive { get; set; }
        internal List<EventStreamOwnershipRecord> OtherOwners { get; } = [];
        private readonly ServiceProvider _provider;
        private readonly KafkaConsumerLagObserver _lag;

        internal async Task<KafkaConnectEventDeliveryRollbackReadinessReader> MultiStreamTerminalAsync(bool completed)
        {
            var reader = CreateReader();
            await reader.PrepareAsync(EventType, 1, Generation, new(_ => Task.CompletedTask));
            Journal.Operation = Journal.Operation with { Phase = completed ? EventDeliveryRollbackPhase.Completed : EventDeliveryRollbackPhase.Recovered };
            Owner = Owner with { CurrentOwner = completed ? EventDeliveryOwner.LegacyPolling : EventDeliveryOwner.CdcKafka };
            Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(Owner.CdcKafkaBindingJson, out var baseline));
            var other = baseline! with { ConnectorName = "other", TopicName = "messaging.other.v1", ActivationId = Guid.CreateVersion7(),
                ShadowConnectorName = "other-shadow", ShadowTopicName = "fullnet.shadow.other.v1",
                Topics = TestTopics(["messaging.other.v1", KafkaTopicNames.GetRetryTopic("messaging.other.v1", "5s")]) };
            OtherOwners.Add(Owner with { MessageType = "fullnet.messaging.other.changed", TopicCode = "messaging.other.v1", CurrentOwner = EventDeliveryOwner.CdcKafka, CdcKafkaBindingJson = other.ToJson() });
            Paused = !completed; Connect.ClearReceivedCalls();
            return reader;
        }

        internal void ChangeOther(string fault)
        {
            Assert.IsTrue(CdcKafkaDeliveryBinding.TryParse(OtherOwners[0].CdcKafkaBindingJson, out var baseline));
            var other = baseline!;
            other = fault switch
            {
                "connector" => other with { ConnectorName = "orders" },
                "shadow_connector" => other with { ShadowConnectorName = "orders" },
                "topic" => other with { Topics = TestTopics(["messaging.orders.v1"]) },
                "topic_id" => other with { Topics = [new("messaging.other.v1", "original-messaging.orders.v1", [new(0, 0, 0)])] },
                "shadow_topic" => other with { ShadowTopicName = KafkaTopicNames.GetRetryTopic("messaging.orders.v1", "5s") },
                "missing_shadow" => other with { ShadowConnectorName = null },
                "endpoint" => other with { ConnectBaseUri = "http://replacement:8083/" },
                "environment" => other with { EnvironmentName = "Other" },
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            };
            OtherOwners[0] = OtherOwners[0] with { CdcKafkaBindingJson = other.ToJson() };
        }

        internal ControlFixture(bool includeOtherDefaultCdc = false)
        {
            Journal = new(new(Generation, EventType, 1, EventDeliveryRollbackPhase.Prepared, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            var store = Substitute.For<IEventStreamOwnershipStore>();
            var snapshot = new CdcKafkaDeliveryBinding("test-cluster", "orders", KafkaConnectResourceIdentity.Compute(ConnectorConfiguration()), "messaging.orders.v1",
                ["fullnet.messaging.orders.a", "fullnet.messaging.orders.b"], ["5s"], TestTopics(["messaging.orders.v1", KafkaTopicNames.GetRetryTopic("messaging.orders.v1", "5s")]))
                { ActivationId = Guid.CreateVersion7(), ConnectBaseUri = "http://connect:8083/", EnvironmentName = "Testing",
                    ShadowConnectorName = "orders-shadow", ShadowTopicName = "fullnet.shadow.orders.v1" };
            Owner = new EventStreamOwnershipRecord(EventType, 1, "messaging.orders.v1", EventDeliveryOwner.CdcKafka,
                EventDeliveryOwner.LegacyPolling, Guid.Empty, DateTimeOffset.UtcNow, null, null, "test", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                { CdcKafkaBindingJson = snapshot.ToJson() };
            store.FindAsync(EventType, 1, Arg.Any<CancellationToken>()).Returns(_ => Owner);
            store.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => new[] { Owner }.Concat(OtherOwners).ToArray());
            store.IsRollbackActiveAsync(EventType, 1, Arg.Any<CancellationToken>()).Returns(_ => RollbackActive);
            var topic = IntegrationEventTopicDefinition.Create("messaging.orders.v1", EventType, 1, EventDeliveryOwner.LegacyPolling);
            var subscriptions = new[] { Subscription("fullnet.messaging.orders.a"), Subscription("fullnet.messaging.orders.b") };
            var catalog = new IntegrationEventSubscriptionCatalog([topic], subscriptions);
            var fence = Fence;
            fence.TryReadAsync(EventType, 1, Generation, Arg.Any<CancellationToken>()).Returns(new EventDeliveryProducerFenceSnapshot(
                Generation, CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
            var services = new ServiceCollection();
            Source.ReadSourcePositionAsync(Arg.Any<string>(), Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>())
                .Returns(new CdcSourcePositionHealthSnapshot(true, null));
            services.AddSingleton(Source);
            services.AddSingleton(Substitute.For<ICurrentTenantContextWriter>());
            services.AddSingleton<IEventDeliveryRollbackJournal>(Journal);
            services.AddSingleton<IEventStreamOwnershipStore>(store);
            services.AddSingleton<IIntegrationEventSubscriptionCatalog>(catalog);
            services.AddSingleton(topic);
            if (includeOtherDefaultCdc) services.AddSingleton(IntegrationEventTopicDefinition.Create(
                "messaging.other.v1", "fullnet.messaging.other.changed", 1, EventDeliveryOwner.CdcKafka));
            var resolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
            resolver.GetDeliveryOwnersAsync(Arg.Any<IReadOnlyCollection<IntegrationEventTopicDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(string, int), EventDeliveryOwner> { [(EventType, 1)] = EventDeliveryOwner.CdcKafka });
            if (includeOtherDefaultCdc) resolver.GetDeliveryOwnersAsync(Arg.Any<IReadOnlyCollection<IntegrationEventTopicDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(string, int), EventDeliveryOwner> { [(EventType, 1)] = EventDeliveryOwner.CdcKafka, [("fullnet.messaging.other.changed", 1)] = EventDeliveryOwner.CdcKafka });
            services.AddSingleton(resolver);
            services.AddSingleton(fence);
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns("Testing");
            services.AddSingleton(environment);
            _provider = services.BuildServiceProvider();
            Connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 10));
            Connect.WaitForConnectorHealthyAsync("orders", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
            Connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(ConnectorConfiguration());
            Connect.TryReadKafkaClusterIdAsync(Arg.Any<CancellationToken>()).Returns("test-cluster");
            Connect.PauseConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ => { Paused = true; return Task.CompletedTask; });
            Connect.ResumeConnectorAsync("orders", Arg.Any<CancellationToken>()).Returns(_ => { Paused = false; return Task.CompletedTask; });
            Connect.IsConnectorPausedAsync("orders", Arg.Any<CancellationToken>()).Returns(_ => Paused);
            _lag = new(_ =>
            {
                var client = Substitute.For<IKafkaLagQueryClient>();
                client.ReadClusterIdAsync().Returns(_ => BrokerClusterId);
                client.ReadTopicBindingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var names = call.Arg<IReadOnlyList<string>>()!;
                    if (UnknownRetry && Paused && names.Any(name => name.Contains("retry", StringComparison.Ordinal))) return null;
                    return TestTopics(names).Select(item => item.TopicName == RecreatedTopicName ? item with { TopicId = "recreated" }
                        : WindowChanged ? item with { Partitions = [new(0, 1, 1)] } : item).ToArray();
                });
                client.ReadPartitionsAsync(Arg.Any<string>()).Returns(call =>
                    UnknownRetry && call.Arg<string>()!.Contains("retry", StringComparison.Ordinal)
                        ? Array.Empty<TopicPartition>() : new[] { new TopicPartition(call.Arg<string>()!, 0) });
                client.ReadCommittedOffsetsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                {
                    var partitions = call.Arg<IReadOnlyList<TopicPartition>>()!;
                    Queries.Add(call.Arg<string>() + "|" + partitions[0].Topic);
                    return partitions.ToDictionary(item => item, _ => 0L);
                });
                client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                    call.Arg<IReadOnlyList<TopicPartition>>()!.ToDictionary(item => item, _ => 0L));
                return client;
            });
        }

        internal KafkaConnectEventDeliveryRollbackReadinessReader CreateReader(IKafkaConnectAdminClient? connect = null, string[]? retryStages = null) => new(
            _provider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new KafkaMessagingOptions { Enabled = true, RetryStages = retryStages ?? ["5s"] }), connect ?? Connect, _lag,
            _provider.GetRequiredService<IHostEnvironment>());

        internal static KafkaConnectRollbackOptions CreateOptions() => new()
        {
            Enabled = true, ConnectBaseUri = "http://connect:8083", DrainPollIntervalMilliseconds = 10,
            Streams = [new() { EventType = EventType, SchemaVersion = 1, ConnectorName = "orders", TopicName = "messaging.orders.v1",
                ShadowTopicName = "fullnet.shadow.orders.v1", ShadowConnectorName = "orders-shadow", ConsumerGroupId = "fullnet.messaging.orders.a", SourceDatabaseName = "test" }],
        };

        private static CdcKafkaTopicBinding[] TestTopics(IReadOnlyList<string> topics, string prefix = "original-") =>
            topics.Select(name => new CdcKafkaTopicBinding(name, prefix + name, [new(0, 0, 0)])).ToArray();

        internal static Dictionary<string, string> ConnectorConfiguration() => new()
        {
            ["connector.class"] = "io.debezium.connector.mysql.MySqlConnector", ["database.hostname"] = "original-database-server",
            ["database.include.list"] = "test", ["table.include.list"] = "test.fn_messaging_outbox_event", ["database.password"] = "credential-v1",
            ["key.converter"] = "org.apache.kafka.connect.storage.StringConverter",
            ["value.converter"] = "io.debezium.converters.BinaryDataConverter",
            ["value.converter.delegate.converter.type"] = "org.apache.kafka.connect.json.JsonConverter",
            ["value.converter.delegate.converter.type.schemas.enable"] = "false",
            ["topic.heartbeat.prefix"] = "fullnet.test.internal.heartbeat.mysql", ["heartbeat.interval.ms"] = "1000",
            ["topic.prefix"] = "source.connector", ["transforms"] = "fullnet_filter,outbox", ["tasks.max"] = "1", ["snapshot.mode"] = "no_data",
            ["transforms.fullnet_filter.type"] = "net.full.connect.FullNetOutboxStreamFilter",
            ["transforms.fullnet_filter.source.topic"] = "source.connector.test.fn_messaging_outbox_event",
            ["transforms.fullnet_filter.event.type"] = EventType, ["transforms.fullnet_filter.schema.version"] = "1",
            ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
            ["transforms.outbox.table.field.event.id"] = "Id", ["transforms.outbox.table.field.event.key"] = "PartitionKey",
            ["transforms.outbox.table.field.event.type"] = "MessageType", ["transforms.outbox.table.field.event.payload"] = "Payload",
            ["transforms.outbox.route.by.field"] = "MessageType", ["transforms.outbox.route.topic.replacement"] = "messaging.orders.v1",
            ["transforms.outbox.table.op.invalid.behavior"] = "fatal",
            ["transforms.outbox.table.fields.additional.placement"] = "Id:header:event_id,MessageType:header:message_type,SchemaVersion:header:schema_version,ContentType:header:content_type,TenantId:header:tenant_id,CorrelationId:header:correlation_id,CausationId:header:causation_id,TraceParent:header:trace_parent,Producer:header:producer,OccurredAtUtc:header:occurred_at_utc",
        };

        private static IIntegrationEventSubscription Subscription(string group)
        {
            var subscription = Substitute.For<IIntegrationEventSubscription>();
            subscription.ConsumerName.Returns(group);
            subscription.EventType.Returns(EventType);
            subscription.SchemaVersion.Returns(1);
            subscription.IdempotencyStrategy.Returns(IntegrationEventIdempotencyStrategy.MessageIdDeduplication);
            return subscription;
        }
        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    private sealed class MemoryJournal(EventDeliveryRollbackOperation operation) : IEventDeliveryRollbackJournal
    {
        internal EventDeliveryRollbackOperation Operation { get; set; } = operation;
        internal bool CurrentTerminalEnabled { get; set; } = true;
        public Task<EventDeliveryRollbackOperation?> FindCurrentTerminalAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult<EventDeliveryRollbackOperation?>(CurrentTerminalEnabled && Operation.Phase is EventDeliveryRollbackPhase.Recovered or EventDeliveryRollbackPhase.Completed ? Operation : null);
        public Task<EventDeliveryRollbackOperation?> FindAsync(Guid generation, CancellationToken cancellationToken = default) =>
            Task.FromResult<EventDeliveryRollbackOperation?>(generation == Operation.Id ? Operation : null);
        public Task<bool> BindResourcesAsync(Guid generation, string resourcesJson, CancellationToken cancellationToken = default)
        {
            if (generation != Operation.Id || Operation.Phase != EventDeliveryRollbackPhase.Prepared || Operation.ResourcesJson is not null) return Task.FromResult(false);
            Operation = Operation with { ResourcesJson = resourcesJson };
            return Task.FromResult(true);
        }
        public Task<bool> TransitionAsync(Guid generation, EventDeliveryRollbackPhase expected, EventDeliveryRollbackPhase next,
            CancellationToken cancellationToken = default)
        {
            if (generation != Operation.Id || Operation.Phase != expected) return Task.FromResult(false);
            Operation = Operation with { Phase = next, HasPauseIntent = Operation.HasPauseIntent || next == EventDeliveryRollbackPhase.PauseRequested };
            return Task.FromResult(true);
        }
    }

    private sealed class ControlHttpHandler : HttpMessageHandler
    {
        internal bool Paused { get; private set; }
        internal bool FailResumeTasks { get; set; }
        internal int ResumeCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/pause", StringComparison.Ordinal))
            {
                Paused = true;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            }
            if (path.EndsWith("/resume", StringComparison.Ordinal))
            {
                ResumeCalls++;
                Paused = false;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
            }
            var json = path == "/" ? "{\"kafka_cluster_id\":\"test-cluster\"}"
                : path.EndsWith("/config", StringComparison.Ordinal) ? System.Text.Json.JsonSerializer.Serialize(ControlFixture.ConnectorConfiguration())
                : path.EndsWith("/offsets", StringComparison.Ordinal) ? "{\"offsets\":[{\"offset\":{\"file\":\"mysql-bin.000001\",\"pos\":10}}]}"
                : Paused ? "{\"connector\":{\"state\":\"PAUSED\"},\"tasks\":[{\"state\":\"PAUSED\"}]}"
                : FailResumeTasks ? "{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"FAILED\"}]}"
                : "{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"RUNNING\"}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
