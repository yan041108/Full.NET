using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestNotificationProgressReaderTests
{
    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Final_business_receipt_reads_only_its_own_notification(string status)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var submission = f.Submission with { FinalStatus = status };
        var result = await f.Reader.GetAsync(submission, cancellation.Token);
        Assert.AreSame(f.Snapshot, result);
        await f.Directory.Received(1).FindByIdempotencyAsync("workflow", $"workflow-{submission.LastMessageId:N}", cancellation.Token);
        Assert.AreEqual(1, f.Directory.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Pending_business_receipt_does_not_guess_notification_identity()
    {
        var f = new Fixture();
        Assert.IsNull(await f.Reader.GetAsync(f.Submission with { FinalStatus = null, CompletedAtUtc = null, LastMessageId = null }));
        Assert.AreEqual(0, f.Directory.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("host")]
    [DataRow("unavailable")]
    [DataRow("message")]
    [DataRow("completion")]
    [DataRow("status")]
    public async Task Untrusted_or_incomplete_receipt_never_reads_notifications(string kind)
    {
        var f = new Fixture(); var submission = f.Submission;
        if (kind == "tenant") submission = submission with { TenantId = Guid.NewGuid() };
        if (kind == "host") f.Tenant.IsHost.Returns(true);
        if (kind == "unavailable") f.Tenant.IsAvailable.Returns(false);
        if (kind == "message") submission = submission with { LastMessageId = Guid.Empty };
        if (kind == "completion") submission = submission with { CompletedAtUtc = null };
        if (kind == "status") submission = submission with { FinalStatus = "Failed" };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Reader.GetAsync(submission));
        Assert.AreEqual(0, f.Directory.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Accepted_business_result_can_still_have_no_notification_intent()
    {
        var f = new Fixture(); f.Directory.FindByIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((NotificationIntentDeliverySnapshot?)null);
        Assert.IsNull(await f.Reader.GetAsync(f.Submission));
        Assert.AreEqual(1, f.Directory.ReceivedCalls().Count());
        Assert.AreEqual("Approved", f.Submission.FinalStatus);
    }

    [TestMethod]
    public async Task Notification_query_failure_preserves_business_receipt_and_propagates()
    {
        var f = new Fixture(); var expected = new InvalidOperationException("owner unavailable");
        f.Directory.FindByIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<NotificationIntentDeliverySnapshot?>(expected));
        Assert.AreSame(expected, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Reader.GetAsync(f.Submission)));
        Assert.AreEqual("Approved", f.Submission.FinalStatus);
        Assert.AreEqual(1, f.Directory.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly Guid TenantId = Guid.NewGuid();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly INotificationIntentDeliveryDirectory Directory = Substitute.For<INotificationIntentDeliveryDirectory>();
        internal readonly EnterpriseRequestApprovalSubmission Submission;
        internal readonly NotificationIntentDeliverySnapshot Snapshot = new(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, 1, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow);
        internal readonly EnterpriseRequestNotificationProgressReader Reader;
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Submission = new(Guid.NewGuid(), TenantId, Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Request",
                DateTimeOffset.UtcNow, null, "Approved", DateTimeOffset.UtcNow, Guid.NewGuid());
            Directory.FindByIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Snapshot);
            Reader = new(Directory, Tenant);
        }
    }
}
