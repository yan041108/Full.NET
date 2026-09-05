using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationDeliveryStateMachineTests
{
    [TestMethod]
    public void Provider_sent_is_not_delivered_and_unknown_does_not_auto_succeed()
    {
        var sent = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Accepted,
            NotificationDeliveryStatus.Sent,
            NotificationStatusSource.Provider);
        var unknown = NotificationDeliveryStateMachine.Apply(
            sent.Status,
            NotificationDeliveryStatus.Unknown,
            NotificationStatusSource.Provider);
        var guessedDelivered = NotificationDeliveryStateMachine.Apply(
            unknown.Status,
            NotificationDeliveryStatus.Delivered,
            NotificationStatusSource.Provider);

        Assert.AreEqual(NotificationDeliveryStatus.Sent, sent.Status);
        Assert.AreEqual(NotificationDeliveryStatus.Unknown, unknown.Status);
        Assert.IsFalse(guessedDelivered.Applied);
        Assert.AreEqual(NotificationDeliveryStatus.Unknown, guessedDelivered.Status);
        Assert.AreEqual(NotificationsErrorCodes.DeliveryUntrustedDelivered, guessedDelivered.ErrorCode);
    }

    [TestMethod]
    public void Trusted_receipt_advances_monotonically_to_delivered_then_read()
    {
        var delivered = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Sent,
            NotificationDeliveryStatus.Delivered,
            NotificationStatusSource.Receipt);
        var read = NotificationDeliveryStateMachine.Apply(
            delivered.Status,
            NotificationDeliveryStatus.Read,
            NotificationStatusSource.User);

        Assert.IsTrue(delivered.Applied);
        Assert.AreEqual(NotificationDeliveryStatus.Delivered, delivered.Status);
        Assert.AreEqual(NotificationDeliveryStatus.Read, read.Status);
    }

    [TestMethod]
    public void Out_of_order_or_duplicate_receipts_do_not_regress_terminal_status()
    {
        var delivered = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Sent,
            NotificationDeliveryStatus.Delivered,
            NotificationStatusSource.Receipt);
        var duplicate = NotificationDeliveryStateMachine.Apply(
            delivered.Status,
            NotificationDeliveryStatus.Delivered,
            NotificationStatusSource.Receipt);
        var staleSent = NotificationDeliveryStateMachine.Apply(
            delivered.Status,
            NotificationDeliveryStatus.Sent,
            NotificationStatusSource.Receipt);
        var failedAfterDelivered = NotificationDeliveryStateMachine.Apply(
            delivered.Status,
            NotificationDeliveryStatus.Failed,
            NotificationStatusSource.Receipt);

        Assert.IsTrue(duplicate.IsDuplicate);
        Assert.AreEqual(NotificationDeliveryStatus.Delivered, duplicate.Status);
        Assert.IsFalse(staleSent.Applied);
        Assert.AreEqual(NotificationDeliveryStatus.Delivered, staleSent.Status);
        Assert.IsFalse(failedAfterDelivered.Applied);
        Assert.AreEqual(NotificationDeliveryStatus.Delivered, failedAfterDelivered.Status);
    }

    [TestMethod]
    public void Bounce_receipt_can_fail_sent_delivery()
    {
        var failed = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Sent,
            NotificationDeliveryStatus.Failed,
            NotificationStatusSource.Receipt);

        Assert.IsTrue(failed.Applied);
        Assert.AreEqual(NotificationDeliveryStatus.Failed, failed.Status);
    }

    [TestMethod]
    public void Failed_can_dead_letter_but_inbox_read_can_happen_from_persisted()
    {
        var deadLetter = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Failed,
            NotificationDeliveryStatus.DeadLettered,
            NotificationStatusSource.Operator);
        var inboxRead = NotificationDeliveryStateMachine.Apply(
            NotificationDeliveryStatus.Persisted,
            NotificationDeliveryStatus.Read,
            NotificationStatusSource.User);

        Assert.AreEqual(NotificationDeliveryStatus.DeadLettered, deadLetter.Status);
        Assert.AreEqual(NotificationDeliveryStatus.Read, inboxRead.Status);
    }
}
