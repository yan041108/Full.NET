using System.Diagnostics.Metrics;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Full.NET.Realtime.SignalR.Health;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
[DoNotParallelize]
public sealed class SignalRRealtimePublisherTests
{
    [TestMethod]
    public async Task Publish_to_user_passes_cancellation_to_signalr()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                nameof(IFullNetNotificationClient.ReceiveMessageAsync),
                Arg.Any<object?[]>(),
                cancellation.Token)
            .Returns(Task.FromCanceled(cancellation.Token));
        var publisher = CreatePublisher(clientProxy, out var hubClients);
        var userId = Guid.CreateVersion7();
        var message = CreateMessage();

        var exception =
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(
                () => publisher.PublishToUserAsync(
                    userId,
                    message,
                    cancellation.Token));

        Assert.AreEqual(
            cancellation.Token,
            exception.CancellationToken);
        _ = hubClients.Received(1).Group(
            RealtimeGroups.User(userId));
        await clientProxy.Received(1).SendCoreAsync(
            nameof(IFullNetNotificationClient.ReceiveMessageAsync),
            Arg.Is<object?[]>(arguments =>
                HasSingleMessageArgument(arguments, message)),
            cancellation.Token);
    }

    [TestMethod]
    public async Task Successful_group_publish_records_bounded_metrics()
    {
        using var capture = new MetricCapture();
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var publisher = CreatePublisher(clientProxy, out _);

        await publisher.PublishToGroupAsync(
            RealtimeGroups.HostBroadcast,
            CreateMessage());

        AssertOutcome(
            capture,
            target: "group",
            outcome: "success");
    }

    [TestMethod]
    public async Task Publish_failure_records_failure_and_preserves_exception()
    {
        using var capture = new MetricCapture();
        var expected = new InvalidOperationException(
            "Redis publish failed.");
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));
        var publisher = CreatePublisher(clientProxy, out _);

        var actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => publisher.PublishToUserAsync(
                    Guid.CreateVersion7(),
                    CreateMessage()));

        Assert.AreSame(expected, actual);
        AssertOutcome(
            capture,
            target: "user",
            outcome: "failure");
    }

    [TestMethod]
    public async Task Publish_timeout_records_timeout_and_preserves_exception()
    {
        using var capture = new MetricCapture();
        var expected = new TimeoutException(
            "Redis endpoint details must not become metric tags.");
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));
        var publisher = CreatePublisher(clientProxy, out _);

        var actual = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => publisher.PublishToGroupAsync(
                RealtimeGroups.HostBroadcast,
                CreateMessage()));

        Assert.AreSame(expected, actual);
        AssertOutcome(
            capture,
            target: "group",
            outcome: "timeout");
        Assert.IsFalse(capture.LongMeasurements.Any(item =>
            HasTag(item.Tags, "outcome", "failure")));
    }

    [TestMethod]
    public async Task Caller_cancellation_records_canceled_not_failure()
    {
        using var capture = new MetricCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                cancellation.Token)
            .Returns(Task.FromCanceled(cancellation.Token));
        var publisher = CreatePublisher(clientProxy, out _);

        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => publisher.PublishToGroupAsync(
                RealtimeGroups.HostBroadcast,
                CreateMessage(),
                cancellation.Token));

        AssertOutcome(
            capture,
            target: "group",
            outcome: "canceled");
        Assert.IsFalse(capture.LongMeasurements.Any(item =>
            HasTag(item.Tags, "outcome", "failure")));
    }

    [TestMethod]
    public async Task Metric_listener_failure_does_not_change_publish_result()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name ==
                    RealtimeBackplaneTelemetry.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, _, _) =>
                throw new InvalidOperationException(
                    "模拟指标消费者失败。"));
        listener.SetMeasurementEventCallback<double>(
            (_, _, _, _) =>
                throw new InvalidOperationException(
                    "模拟指标消费者失败。"));
        listener.Start();
        var clientProxy = Substitute.For<IClientProxy>();
        clientProxy
            .SendCoreAsync(
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var publisher = CreatePublisher(clientProxy, out _);

        await publisher.PublishToGroupAsync(
            RealtimeGroups.HostBroadcast,
            CreateMessage());
    }

    private static SignalRRealtimePublisher CreatePublisher(
        IClientProxy clientProxy,
        out IHubClients hubClients)
    {
        hubClients = Substitute.For<IHubClients>();
        hubClients
            .Group(Arg.Any<string>())
            .Returns(clientProxy);
        var hubContext =
            Substitute.For<IHubContext<FullNetNotificationHub>>();
        hubContext.Clients.Returns(hubClients);
        return new SignalRRealtimePublisher(hubContext);
    }

    private static RealtimeMessage CreateMessage() =>
        new(
            "fullnet.notifications.inbox_message_received",
            new Dictionary<string, object?>
            {
                ["unreadCount"] = 1,
            });

    private static void AssertOutcome(
        MetricCapture capture,
        string target,
        string outcome)
    {
        var attempts = capture.LongMeasurements.Single(item =>
            item.Name == "fullnet.realtime.publish.attempts");
        Assert.AreEqual(1L, attempts.Value);
        AssertTags(attempts.Tags, target, outcome);

        var duration = capture.DoubleMeasurements.Single(item =>
            item.Name == "fullnet.realtime.publish.duration");
        Assert.IsGreaterThanOrEqualTo(0d, duration.Value);
        AssertTags(duration.Tags, target, outcome);
    }

    private static void AssertTags(
        KeyValuePair<string, object?>[] tags,
        string target,
        string outcome)
    {
        CollectionAssert.AreEquivalent(
            new[] { "target", "outcome" },
            tags.Select(tag => tag.Key).ToArray());
        Assert.IsTrue(HasTag(tags, "target", target));
        Assert.IsTrue(HasTag(tags, "outcome", outcome));
    }

    private static bool HasTag(
        IEnumerable<KeyValuePair<string, object?>> tags,
        string key,
        string value) =>
        tags.Any(tag =>
            tag.Key == key
            && string.Equals(
                tag.Value as string,
                value,
                StringComparison.Ordinal));

    private static bool HasSingleMessageArgument(
        object?[]? arguments,
        RealtimeMessage message) =>
        arguments is not null
        && arguments.Length == 1
        && ReferenceEquals(arguments[0], message);

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name ==
                    RealtimeBackplaneTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                    LongMeasurements.Add(
                        new LongMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) =>
                    DoubleMeasurements.Add(
                        new DoubleMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.Start();
        }

        public List<LongMeasurement> LongMeasurements { get; } = [];

        public List<DoubleMeasurement> DoubleMeasurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record LongMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed record DoubleMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
