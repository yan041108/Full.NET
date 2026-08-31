using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationPolicyTests
{
    [TestMethod]
    public void Mandatory_bypasses_channel_opt_out_and_can_send_during_quiet_hours_when_emergency()
    {
        var optedOut = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Mandatory,
            new NotificationPreferenceSnapshot(ChannelOptedOut: true, MarketingConsentGranted: false, InQuietHours: false),
            emergencyOverride: false);
        var emergencyQuiet = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Mandatory,
            new NotificationPreferenceSnapshot(ChannelOptedOut: false, MarketingConsentGranted: false, InQuietHours: true),
            emergencyOverride: true);

        Assert.IsTrue(optedOut.ShouldDispatchNow);
        Assert.IsFalse(optedOut.IsSuppressed);
        Assert.IsTrue(emergencyQuiet.ShouldDispatchNow);
        Assert.IsFalse(emergencyQuiet.ShouldDelayForQuietHours);
    }

    [TestMethod]
    public void Transactional_outranks_informational_opt_out_but_still_delays_for_quiet_hours()
    {
        var transactional = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Transactional,
            new NotificationPreferenceSnapshot(ChannelOptedOut: true, MarketingConsentGranted: false, InQuietHours: true),
            emergencyOverride: false);
        var informational = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Informational,
            new NotificationPreferenceSnapshot(ChannelOptedOut: true, MarketingConsentGranted: false, InQuietHours: false),
            emergencyOverride: false);

        Assert.IsFalse(transactional.IsSuppressed);
        Assert.IsTrue(transactional.ShouldDelayForQuietHours);
        Assert.IsFalse(transactional.ShouldDispatchNow);
        Assert.IsTrue(informational.IsSuppressed);
        Assert.AreEqual(NotificationsErrorCodes.PolicySuppressed, informational.SuppressionReasonCode);
    }

    [TestMethod]
    public void Marketing_without_consent_is_suppressed_and_cannot_be_force_enabled_by_routing()
    {
        var missingConsent = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Marketing,
            new NotificationPreferenceSnapshot(ChannelOptedOut: false, MarketingConsentGranted: false, InQuietHours: false),
            emergencyOverride: true);
        var consentedButOptedOut = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Marketing,
            new NotificationPreferenceSnapshot(ChannelOptedOut: true, MarketingConsentGranted: true, InQuietHours: false),
            emergencyOverride: false);

        Assert.IsTrue(missingConsent.IsSuppressed);
        Assert.AreEqual(NotificationsErrorCodes.PolicyMarketingConsentRequired, missingConsent.SuppressionReasonCode);
        Assert.IsTrue(consentedButOptedOut.IsSuppressed);
        Assert.AreEqual(NotificationsErrorCodes.PolicySuppressed, consentedButOptedOut.SuppressionReasonCode);
    }

    [TestMethod]
    public void Informational_and_marketing_delay_in_quiet_hours_when_they_are_otherwise_allowed()
    {
        var informational = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Informational,
            new NotificationPreferenceSnapshot(ChannelOptedOut: false, MarketingConsentGranted: false, InQuietHours: true),
            emergencyOverride: false);
        var marketing = NotificationPolicy.Evaluate(
            NotificationPolicyCategory.Marketing,
            new NotificationPreferenceSnapshot(ChannelOptedOut: false, MarketingConsentGranted: true, InQuietHours: true),
            emergencyOverride: false);

        Assert.IsTrue(informational.ShouldDelayForQuietHours);
        Assert.IsFalse(informational.IsSuppressed);
        Assert.IsTrue(marketing.ShouldDelayForQuietHours);
        Assert.IsFalse(marketing.IsSuppressed);
    }
}
