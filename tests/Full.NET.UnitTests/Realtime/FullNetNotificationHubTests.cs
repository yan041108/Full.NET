using System.Diagnostics.Metrics;
using System.Security.Claims;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Full.NET.Realtime.SignalR.Health;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
[DoNotParallelize]
public sealed class FullNetNotificationHubTests
{
    private static readonly Guid UserId =
        Guid.Parse("01912345-6789-7abc-8def-0123456789ab");
    private static readonly Guid TenantId =
        Guid.Parse("01912345-6789-7abc-8def-0123456789cd");

    [TestMethod]
    public async Task Host_scope_joins_user_and_host_broadcast_groups()
    {
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync(
            "connection-1",
            RealtimeGroups.User(UserId),
            Arg.Any<CancellationToken>());
        await groups.Received(1).AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.DidNotReceive().Abort();
    }

    [TestMethod]
    public async Task Matching_tenant_scope_joins_user_and_tenant_groups()
    {
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", $"tenant:{TenantId:N}"),
            new Claim("fullnet_tenant_id", TenantId.ToString("D")));

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync(
            "connection-1",
            RealtimeGroups.User(UserId),
            Arg.Any<CancellationToken>());
        await groups.Received(1).AddToGroupAsync(
            "connection-1",
            RealtimeGroups.Tenant(TenantId),
            Arg.Any<CancellationToken>());
        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.DidNotReceive().Abort();
    }

    [TestMethod]
    public async Task Tenant_scope_without_tenant_claim_aborts_without_joining_host_broadcast()
    {
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", $"tenant:{TenantId:N}"));

        await hub.OnConnectedAsync();

        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.Received(1).Abort();
    }

    [TestMethod]
    public async Task Mismatched_tenant_scope_aborts_without_joining_broadcast()
    {
        var differentTenantId =
            Guid.Parse("01912345-6789-7abc-8def-0123456789ef");
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", $"tenant:{TenantId:N}"),
            new Claim(
                "fullnet_tenant_id",
                differentTenantId.ToString("D")));

        await hub.OnConnectedAsync();

        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.Tenant(differentTenantId),
            Arg.Any<CancellationToken>());
        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.Received(1).Abort();
    }

    [TestMethod]
    public async Task Host_scope_without_valid_subject_aborts_without_joining_host_broadcast()
    {
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", "not-a-user-id"),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();

        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.Received(1).Abort();
    }

    [TestMethod]
    public async Task Missing_fullnet_scope_aborts_without_joining_groups()
    {
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")));

        await hub.OnConnectedAsync();

        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.User(UserId),
            Arg.Any<CancellationToken>());
        await groups.DidNotReceive().AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
        hub.Context.Received(1).Abort();
    }

    [TestMethod]
    [DataRow("subject", "rejected_invalid_subject")]
    [DataRow("scope", "rejected_scope_claim_mismatch")]
    [DataRow("tenant", "rejected_scope_claim_mismatch")]
    public async Task Duplicate_security_claims_abort_without_joining_groups(
        string duplicateClaim,
        string expectedOutcome)
    {
        using var capture = new MetricCapture();
        Claim[] claims = duplicateClaim switch
        {
            "subject" =>
            [
                new Claim("sub", UserId.ToString("D")),
                new Claim("sub", TenantId.ToString("D")),
                new Claim("fullnet_scope", "host"),
            ],
            "scope" =>
            [
                new Claim("sub", UserId.ToString("D")),
                new Claim("fullnet_scope", "host"),
                new Claim(
                    "fullnet_scope",
                    $"tenant:{TenantId:N}"),
            ],
            "tenant" =>
            [
                new Claim("sub", UserId.ToString("D")),
                new Claim(
                    "fullnet_scope",
                    $"tenant:{TenantId:N}"),
                new Claim(
                    "fullnet_tenant_id",
                    TenantId.ToString("D")),
                new Claim(
                    "fullnet_tenant_id",
                    UserId.ToString("D")),
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(duplicateClaim),
                duplicateClaim,
                message: null),
        };
        var groups = CreateGroups();
        var hub = CreateHub(groups, claims);

        await hub.OnConnectedAsync();

        Assert.IsFalse(groups.ReceivedCalls().Any());
        hub.Context.Received(1).Abort();
        AssertDecision(capture, expectedOutcome);
    }

    [TestMethod]
    [DataRow("subject", "rejected_invalid_subject")]
    [DataRow("tenant", "rejected_scope_claim_mismatch")]
    public async Task Empty_security_identifiers_abort_without_joining_groups(
        string emptyIdentifier,
        string expectedOutcome)
    {
        using var capture = new MetricCapture();
        Claim[] claims = emptyIdentifier switch
        {
            "subject" =>
            [
                new Claim("sub", Guid.Empty.ToString("D")),
                new Claim("fullnet_scope", "host"),
            ],
            "tenant" =>
            [
                new Claim("sub", UserId.ToString("D")),
                new Claim(
                    "fullnet_scope",
                    $"tenant:{Guid.Empty:N}"),
                new Claim(
                    "fullnet_tenant_id",
                    Guid.Empty.ToString("D")),
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(emptyIdentifier),
                emptyIdentifier,
                message: null),
        };
        var groups = CreateGroups();
        var hub = CreateHub(groups, claims);

        await hub.OnConnectedAsync();

        Assert.IsFalse(groups.ReceivedCalls().Any());
        hub.Context.Received(1).Abort();
        AssertDecision(capture, expectedOutcome);
    }

    [TestMethod]
    public async Task Host_scope_records_authorized_host_decision()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();

        AssertDecision(capture, "authorized_host");
    }

    [TestMethod]
    public async Task Tenant_scope_records_authorized_tenant_decision()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", $"tenant:{TenantId:N}"),
            new Claim("fullnet_tenant_id", TenantId.ToString("D")));

        await hub.OnConnectedAsync();

        AssertDecision(capture, "authorized_tenant");
    }

    [TestMethod]
    public async Task Authorized_connection_records_active_lifecycle_without_tags()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(exception: null);

        var measurements = capture.LongMeasurements
            .Where(item =>
                item.Name ==
                "fullnet.realtime.hub.connections.active")
            .ToArray();
        Assert.HasCount(2, measurements);
        Assert.AreEqual(1L, measurements[0].Value);
        Assert.AreEqual(-1L, measurements[1].Value);
        Assert.IsTrue(measurements.All(item =>
            item.Tags.Length == 0));
    }

    [TestMethod]
    [DataRow(false, "completed")]
    [DataRow(true, "failure")]
    public async Task Authorized_disconnect_records_bounded_connection_duration(
        bool hasException,
        string expectedOutcome)
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(
            hasException
                ? new InvalidOperationException("Connection failed.")
                : null);

        var measurement = capture.DoubleMeasurements.Single(item =>
            item.Name ==
            "fullnet.realtime.hub.connection.duration");
        Assert.IsGreaterThanOrEqualTo(0D, measurement.Value);
        CollectionAssert.AreEquivalent(
            new[] { "outcome" },
            measurement.Tags
                .Select(tag => tag.Key)
                .ToArray());
        Assert.IsTrue(HasTag(
            measurement.Tags,
            "outcome",
            expectedOutcome));
    }

    [TestMethod]
    public async Task Invalid_subject_records_rejected_subject_decision()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", "not-a-user-id"),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(exception: null);

        AssertDecision(capture, "rejected_invalid_subject");
        Assert.IsFalse(capture.LongMeasurements.Any(item =>
            item.Name ==
            "fullnet.realtime.hub.connections.active"));
        Assert.IsFalse(capture.DoubleMeasurements.Any(item =>
            item.Name ==
            "fullnet.realtime.hub.connection.duration"));
    }

    [TestMethod]
    public async Task Inconsistent_scope_records_rejected_scope_decision()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", $"tenant:{TenantId:N}"));

        await hub.OnConnectedAsync();

        AssertDecision(capture, "rejected_scope_claim_mismatch");
    }

    [TestMethod]
    public async Task Successful_group_assignments_record_bounded_metrics()
    {
        using var capture = new MetricCapture();
        var hub = CreateHub(
            CreateGroups(),
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();

        AssertAssignment(capture, "user", "success");
        AssertAssignment(capture, "broadcast", "success");
    }

    [TestMethod]
    public async Task Failed_group_assignment_records_failure_and_preserves_exception()
    {
        using var capture = new MetricCapture();
        var expected = new InvalidOperationException(
            "Group assignment failed.");
        var groups = CreateGroups();
        groups
            .AddToGroupAsync(
                Arg.Any<string>(),
                RealtimeGroups.HostBroadcast,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        var actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                hub.OnConnectedAsync);

        Assert.AreSame(expected, actual);
        AssertAssignment(capture, "user", "success");
        AssertAssignment(capture, "broadcast", "failure");
        Assert.IsFalse(capture.LongMeasurements.Any(item =>
            item.Name ==
            "fullnet.realtime.hub.connections.active"));
        Assert.IsFalse(capture.DoubleMeasurements.Any(item =>
            item.Name ==
            "fullnet.realtime.hub.connection.duration"));
    }

    [TestMethod]
    public async Task Connection_cancellation_records_canceled_not_failure()
    {
        using var capture = new MetricCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var groups = CreateGroups();
        groups
            .AddToGroupAsync(
                Arg.Any<string>(),
                RealtimeGroups.HostBroadcast,
                cancellation.Token)
            .Returns(Task.FromCanceled(cancellation.Token));
        var hub = CreateHubWithCancellation(
            groups,
            cancellation.Token,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            hub.OnConnectedAsync);

        AssertAssignment(capture, "user", "success");
        AssertAssignment(capture, "broadcast", "canceled");
        Assert.IsFalse(capture.LongMeasurements.Any(item =>
            item.Name ==
            "fullnet.realtime.hub.group.assignments"
            && HasTag(item.Tags, "outcome", "failure")));
    }

    [TestMethod]
    public async Task Metric_listener_failure_does_not_change_group_authorization()
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
        listener.Start();
        var groups = CreateGroups();
        var hub = CreateHub(
            groups,
            new Claim("sub", UserId.ToString("D")),
            new Claim("fullnet_scope", "host"));

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync(
            "connection-1",
            RealtimeGroups.HostBroadcast,
            Arg.Any<CancellationToken>());
    }

    private static void AssertDecision(
        MetricCapture capture,
        string outcome)
    {
        var decision = capture.LongMeasurements.Single(item =>
            item.Name ==
            "fullnet.realtime.hub.authorization.decisions");
        Assert.AreEqual(1L, decision.Value);
        Assert.HasCount(1, decision.Tags);
        Assert.AreEqual("outcome", decision.Tags[0].Key);
        Assert.AreEqual(outcome, decision.Tags[0].Value);
    }

    private static void AssertAssignment(
        MetricCapture capture,
        string target,
        string outcome)
    {
        var assignment = capture.LongMeasurements.Single(item =>
            item.Name ==
            "fullnet.realtime.hub.group.assignments"
            && HasTag(item.Tags, "target", target));
        Assert.AreEqual(1L, assignment.Value);
        AssertAssignmentTags(assignment.Tags, target, outcome);

        var duration = capture.DoubleMeasurements.Single(item =>
            item.Name ==
            "fullnet.realtime.hub.group.assignment.duration"
            && HasTag(item.Tags, "target", target));
        Assert.IsGreaterThanOrEqualTo(0d, duration.Value);
        AssertAssignmentTags(duration.Tags, target, outcome);
    }

    private static void AssertAssignmentTags(
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

    private static IGroupManager CreateGroups()
    {
        var groups = Substitute.For<IGroupManager>();
        groups
            .AddToGroupAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return groups;
    }

    private static FullNetNotificationHub CreateHub(
        IGroupManager groups,
        params Claim[] claims)
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");
        context.ConnectionAborted.Returns(CancellationToken.None);
        context.Items.Returns(new Dictionary<object, object?>());
        context.User.Returns(
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, "Testing")));
        return new FullNetNotificationHub
        {
            Context = context,
            Groups = groups,
        };
    }

    private static FullNetNotificationHub CreateHubWithCancellation(
        IGroupManager groups,
        CancellationToken cancellationToken,
        params Claim[] claims)
    {
        var hub = CreateHub(groups, claims);
        hub.Context.ConnectionAborted.Returns(cancellationToken);
        return hub;
    }

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
