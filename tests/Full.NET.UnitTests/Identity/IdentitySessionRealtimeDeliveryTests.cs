using System.Diagnostics.Metrics;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Observability;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Full.NET.UnitTests.Identity;

[TestClass]
[DoNotParallelize]
public sealed class IdentitySessionRealtimeDeliveryTests
{
    private static readonly Guid UserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000011");
    private static readonly Guid SessionId =
        Guid.Parse("01981a75-f500-7000-8000-000000000012");

    [TestMethod]
    public async Task Publish_sessions_revoked_retries_then_succeeds()
    {
        var publisher = Substitute.For<IRealtimePublisher>();
        var attempts = 0;
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("transient");
                }

                return Task.CompletedTask;
            });
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        await publisher.Received(2).PublishToUserAsync(
            UserId,
            Arg.Is<RealtimeMessage>(message =>
                message != null
                && message.Code == RealtimeMessageCodes.SessionRevoked),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Publish_sessions_revoked_swallows_failure_after_bounded_attempts()
    {
        var publisher = Substitute.For<IRealtimePublisher>();
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("persistent"));
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        await publisher.Received(3).PublishToUserAsync(
            UserId,
            Arg.Any<RealtimeMessage>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Publish_sessions_revoked_records_success_after_retry_metrics()
    {
        using var capture = new MetricCapture();
        var publisher = Substitute.For<IRealtimePublisher>();
        var attempts = 0;
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("transient");
                }

                return Task.CompletedTask;
            });
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        AssertMetricOutcome(capture, "retry", 1);
        AssertMetricOutcome(capture, "success", 1);
    }

    [TestMethod]
    public async Task Publish_sessions_revoked_records_exhausted_metrics()
    {
        using var capture = new MetricCapture();
        var publisher = Substitute.For<IRealtimePublisher>();
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("persistent"));
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        AssertMetricOutcome(capture, "retry", 2);
        AssertMetricOutcome(capture, "exhausted", 1);
    }

    [TestMethod]
    public async Task Metric_listener_failure_does_not_change_delivery_result()
    {
        using var throwingListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IdentitySessionRevokeRealtimeTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        throwingListener.SetMeasurementEventCallback<long>(
            (_, _, _, _) =>
                throw new InvalidOperationException("模拟指标消费者失败。"));
        throwingListener.Start();
        var publisher = Substitute.For<IRealtimePublisher>();
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);
    }

    private static void AssertMetricOutcome(
        MetricCapture capture,
        string outcome,
        long expectedCount)
    {
        var measurements = capture.LongMeasurements
            .Where(item =>
                item.Name == "fullnet.identity.session_revoke.realtime.publish.attempts"
                && item.Tags.Single(tag => tag.Key == "outcome").Value as string == outcome)
            .ToArray();
        Assert.AreEqual(
            expectedCount,
            measurements.Sum(item => item.Value),
            $"Expected outcome '{outcome}' count {expectedCount}.");
        foreach (var measurement in measurements)
        {
            CollectionAssert.AreEquivalent(
                new[] { "outcome" },
                measurement.Tags.Select(tag => tag.Key).ToArray());
        }
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == IdentitySessionRevokeRealtimeTelemetry.MeterName)
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
            _listener.Start();
        }

        public List<LongMeasurement> LongMeasurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record LongMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);
}