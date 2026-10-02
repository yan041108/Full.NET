using Full.NET.Logging.Kafka;
using Full.NET.Hosting.Observability;
using Confluent.Kafka;
using System.Diagnostics.Metrics;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class KafkaLogDeliveryLaneTests
{
    [TestMethod]
    public void Delivery_callback_releases_budget_only_at_terminal_result()
    {
        var client = new FakeClient();
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1, 2, 3], "id-1"));
        Assert.AreEqual(1, lane.ReservedMessages);
        Assert.AreEqual(KafkaLogProduceResult.CapacityExceeded, lane.TryProduce([4], "id-2"));
        client.CompleteNext(true);
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.AcknowledgedCount);
        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([5], "id-3"));
    }

    [TestMethod]
    public void Synchronous_produce_failure_releases_reservation()
    {
        var client = new FakeClient { ThrowOnProduce = true };
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.ProduceRejected, lane.TryProduce([1], "id-1"));
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.FailedCount);
    }

    [TestMethod]
    public void Sdk_queue_full_is_distinct_from_application_capacity_rejection()
    {
        var client = new FakeClient { ThrowQueueFull = true };
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.SdkQueueFull, lane.TryProduce([1], "id-1"));
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.FailedCount);
    }

    [TestMethod]
    public void Invalid_or_oversized_event_key_is_rejected_before_reservation()
    {
        var client = new FakeClient();
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.InvalidEventId, lane.TryProduce([1], new string('汉', 100)));
        Assert.AreEqual(KafkaLogProduceResult.InvalidEventId, lane.TryProduce([1], new string('a', 65)));
        Assert.AreEqual(KafkaLogProduceResult.Oversize, lane.TryProduce(new byte[870], "id-1"));
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(0, client.ProduceCount);
    }

    [TestMethod]
    public void Key_bytes_are_charged_to_pending_budget()
    {
        var client = new FakeClient();
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 140, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.CapacityExceeded, lane.TryProduce(new byte[10], "id-1"));
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(0, client.ProduceCount);
    }

    [TestMethod]
    public void Synchronous_delivery_callback_does_not_leave_a_reservation()
    {
        var client = new FakeClient { CompleteSynchronously = true };
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1], "id-1"));
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.AcknowledgedCount);
    }

    [TestMethod]
    public void Failed_or_repeated_callback_releases_once()
    {
        var client = new FakeClient();
        using var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1], "id-1"));
        client.CompleteNext(false);
        client.CompleteLastAgain(false);
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.FailedCount);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Asynchronous_failure_and_shutdown_abandonment_emit_delivery_metrics()
    {
        long failed = 0;
        long abandoned = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == KafkaLogSnapshotExporter.MeterName
                && instrument.Name == "fullnet_log_kafka_delivery_total")
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key != "outcome")
                {
                    continue;
                }

                if (string.Equals(tag.Value as string, "failed", StringComparison.Ordinal))
                {
                    Interlocked.Add(ref failed, value);
                }
                else if (string.Equals(tag.Value as string, "abandoned", StringComparison.Ordinal))
                {
                    Interlocked.Add(ref abandoned, value);
                }
            }
        });
        listener.Start();

        var client = new FakeClient();
        var lane = new KafkaLogDeliveryLane("logs-general", 2, 1_024, 1_000, client);
        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1], "id-1"));
        client.CompleteNext(false);
        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([2], "id-2"));
        lane.Dispose();

        Assert.AreEqual(1L, Interlocked.Read(ref failed));
        Assert.AreEqual(1L, Interlocked.Read(ref abandoned));
    }

    [TestMethod]
    public void Shutdown_releases_unconfirmed_reservations_and_refuses_new_events()
    {
        var client = new FakeClient();
        var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);

        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1], "id-1"));
        lane.Dispose();
        client.CompleteNext(true);
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.AbandonedCount);
        Assert.AreEqual(0L, lane.AcknowledgedCount);
        Assert.AreEqual(KafkaLogProduceResult.Stopped, lane.TryProduce([2], "id-2"));
        Assert.AreEqual(1, client.FlushCount);
        Assert.AreEqual(1, client.DisposeCount);
    }

    [TestMethod]
    public void Sdk_flush_or_dispose_failure_still_releases_pending_reservations()
    {
        var client = new FakeClient { ThrowOnFlush = true, ThrowOnDispose = true };
        var lane = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, client);
        Assert.AreEqual(KafkaLogProduceResult.Accepted, lane.TryProduce([1], "id-1"));

        lane.Dispose();

        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(1L, lane.AbandonedCount);
        Assert.AreEqual(2L, lane.ShutdownFailureCount);
        Assert.AreEqual(1, client.FlushCount);
        Assert.AreEqual(1, client.DisposeCount);
    }

    [TestMethod]
    public void Separate_lanes_keep_priority_capacity_when_general_is_full()
    {
        using var general = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, new FakeClient());
        using var priority = new KafkaLogDeliveryLane("logs-priority", 1, 1_024, 1_000, new FakeClient());

        Assert.AreEqual(KafkaLogProduceResult.Accepted, general.TryProduce([1], "id-1"));
        Assert.AreEqual(KafkaLogProduceResult.CapacityExceeded, general.TryProduce([2], "id-2"));
        Assert.AreEqual(KafkaLogProduceResult.Accepted, priority.TryProduce([3], "id-3"));
    }

    [TestMethod]
    public void Pair_stops_both_lanes_before_flushing_and_shares_one_deadline()
    {
        var generalClient = new FakeClient();
        var priorityClient = new FakeClient { FlushDelay = TimeSpan.FromMilliseconds(60) };
        var general = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, generalClient);
        var priority = new KafkaLogDeliveryLane("logs-priority", 1, 1_024, 1_000, priorityClient);
        using var pair = new KafkaLogProducerPair(general, priority, TimeSpan.FromMilliseconds(200));
        Assert.AreEqual(KafkaLogProduceResult.Accepted, pair.TryProduce([1], "id-1", highPriority: false));
        Assert.AreEqual(KafkaLogProduceResult.Accepted, pair.TryProduce([2], "id-2", highPriority: true));
        priorityClient.OnFlush = () =>
            Assert.AreEqual(KafkaLogProduceResult.Stopped, general.TryProduce([3], "id-3"));

        pair.Dispose();

        Assert.AreEqual("logs-general", generalClient.LastTopic);
        Assert.AreEqual("logs-priority", priorityClient.LastTopic);
        Assert.IsTrue(generalClient.LastFlushTimeout < priorityClient.LastFlushTimeout);
        Assert.AreEqual(1, generalClient.DisposeCount);
        Assert.AreEqual(1, priorityClient.DisposeCount);
    }

    [TestMethod]
    public void Pair_factory_failure_disposes_the_first_producer()
    {
        var firstClient = new FakeClient();
        var first = new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, firstClient);
        var options = new KafkaLogProducerOptions
        {
            BootstrapServers = "broker:9093",
            GeneralTopic = "logs-general",
            PriorityTopic = "logs-priority",
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            KafkaLogProducerPair.CreateWithFactory(options, (_, highPriority) =>
                highPriority ? throw new InvalidOperationException("second producer failed") : first));
        Assert.AreEqual(1, firstClient.DisposeCount);
    }

    [TestMethod]
    public void Pair_accepts_host_snapshot_without_copying_its_owned_full_array()
    {
        var generalClient = new FakeClient();
        var priorityClient = new FakeClient();
        using var pair = new KafkaLogProducerPair(
            new KafkaLogDeliveryLane("logs-general", 1, 1_024, 1_000, generalClient),
            new KafkaLogDeliveryLane("logs-priority", 1, 1_024, 1_000, priorityClient),
            TimeSpan.FromSeconds(1));
        var payload = new byte[] { 1, 2, 3 };
        var snapshot = new HostLogSnapshot(
            payload,
            Guid.CreateVersion7().ToString("D"),
            isHighPriority: true);

        Assert.AreEqual(KafkaLogProduceResult.Accepted, pair.TryProduce(snapshot));
        Assert.AreSame(payload, priorityClient.LastPayload);
        Assert.AreEqual(0, generalClient.ProduceCount);
    }

    private sealed class FakeClient : IKafkaLogProducerClient
    {
        private readonly Queue<Action<bool>> _callbacks = new();
        private Action<bool>? _last;

        public bool ThrowOnProduce { get; set; }
        public bool ThrowQueueFull { get; set; }
        public bool CompleteSynchronously { get; set; }
        public bool ThrowOnFlush { get; set; }
        public bool ThrowOnDispose { get; set; }
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ProduceCount { get; private set; }
        public string? LastTopic { get; private set; }
        public byte[]? LastPayload { get; private set; }
        public TimeSpan LastFlushTimeout { get; private set; }
        public TimeSpan FlushDelay { get; set; }
        public Action? OnFlush { get; set; }

        public void Produce(string topic, string eventId, byte[] payload, Action<bool> onCompletion)
        {
            ProduceCount++;
            LastTopic = topic;
            LastPayload = payload;
            if (ThrowOnProduce)
            {
                throw new InvalidOperationException("simulated SDK rejection");
            }

            if (ThrowQueueFull)
            {
                throw new KafkaException(new Error(ErrorCode.Local_QueueFull));
            }

            if (CompleteSynchronously)
            {
                onCompletion(true);
                return;
            }

            _callbacks.Enqueue(onCompletion);
        }

        public int Flush(TimeSpan timeout)
        {
            FlushCount++;
            LastFlushTimeout = timeout;
            OnFlush?.Invoke();
            if (FlushDelay > TimeSpan.Zero)
            {
                Thread.Sleep(FlushDelay);
            }
            if (ThrowOnFlush)
            {
                throw new InvalidOperationException("simulated flush failure");
            }

            return _callbacks.Count;
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("simulated dispose failure");
            }
        }

        public void CompleteNext(bool acknowledged)
        {
            _last = _callbacks.Dequeue();
            _last(acknowledged);
        }

        public void CompleteLastAgain(bool acknowledged) => _last!(acknowledged);
    }
}
