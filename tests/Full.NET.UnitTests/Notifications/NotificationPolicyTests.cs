using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features.ManagePreferences;

namespace Full.NET.UnitTests.Notifications;

/// <summary>单元测试（通知）：Notification Policy。</summary>
[TestClass]
public sealed class NotificationPolicyTests
{
    [TestMethod]
    public void Quiet_hours_cross_midnight_and_stop_at_the_declared_boundary()
    {
        const string json = """{"timeZone":"Asia/Shanghai","start":"22:00","end":"08:00"}""";
        var now = new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(NotificationQuietHours.IsInQuietHours(json, now));
        Assert.AreEqual(end, NotificationQuietHours.NextAllowedAt(json, now));
        Assert.IsTrue(NotificationQuietHours.IsInQuietHours(json, end.AddMinutes(-1)));
        Assert.IsFalse(NotificationQuietHours.IsInQuietHours(json, end));
        Assert.AreEqual(end, NotificationQuietHours.NextAllowedAt(json, end));
    }

    [TestMethod]
    public void Quiet_hours_spring_gap_advances_to_the_first_valid_minute()
    {
        const string json = """{"timeZone":"America/New_York","start":"01:00","end":"02:30"}""";
        var now = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
        Assert.IsTrue(NotificationQuietHours.IsInQuietHours(json, now));
        Assert.AreEqual(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero),
            NotificationQuietHours.NextAllowedAt(json, now));
    }

    [TestMethod]
    public void Quiet_hours_half_hour_spring_gap_does_not_assume_one_hour()
    {
        const string json = """{"timeZone":"Australia/Lord_Howe","start":"01:00","end":"02:15"}""";
        var now = new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(NotificationQuietHours.IsInQuietHours(json, now));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 3, 15, 30, 0, TimeSpan.Zero),
            NotificationQuietHours.NextAllowedAt(json, now));
    }

    [TestMethod]
    public void Quiet_hours_fall_overlap_uses_the_later_end_occurrence()
    {
        const string json = """{"timeZone":"America/New_York","start":"00:30","end":"01:30"}""";
        var now = new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero);
        Assert.AreEqual(end, NotificationQuietHours.NextAllowedAt(json, now));
        Assert.IsTrue(NotificationQuietHours.IsInQuietHours(json, end.AddMinutes(-1)));
        Assert.IsFalse(NotificationQuietHours.IsInQuietHours(json, end));
    }

    [TestMethod]
    public void Preference_rejects_unknown_time_zone_and_malformed_or_empty_windows()
    {
        foreach (var json in new[]
        {
            """{"timeZone":"No/Such_Zone","start":"22:00","end":"08:00"}""",
            """{"timeZone":"UTC","start":"08:00","end":"08:00"}""",
            """{"timeZone":"UTC","start":22,"end":"08:00"}""",
            """[]""",
        })
        {
            var result = NotificationPreferenceService.NormalizeQuietHours(json);
            Assert.IsFalse(result.IsSuccess, json);
            Assert.AreEqual(NotificationsErrorCodes.PreferenceValidationFailed, result.Error!.Code);
        }
    }

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
