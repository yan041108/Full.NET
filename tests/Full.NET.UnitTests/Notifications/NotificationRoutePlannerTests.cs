using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationRoutePlannerTests
{
    [TestMethod]
    public void Single_selects_the_only_enabled_profile_and_fails_when_unavailable()
    {
        var selected = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Single,
            [
                Candidate("sms-primary", enabled: true),
                Candidate("sms-backup", enabled: false),
            ]);
        var missing = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Single,
            [Candidate("sms-primary", enabled: false)]);
        var ambiguous = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Single,
            [
                Candidate("sms-primary", enabled: true),
                Candidate("sms-backup", enabled: true),
            ]);

        Assert.IsTrue(selected.IsSuccess);
        Assert.AreEqual("sms-primary", selected.Targets.Single().ProfileKey);
        Assert.AreEqual(NotificationsErrorCodes.RouteProfileUnavailable, missing.ErrorCode);
        Assert.AreEqual(NotificationsErrorCodes.RouteProfileUnavailable, ambiguous.ErrorCode);
    }

    [TestMethod]
    public void FanOut_emits_only_the_explicit_enabled_list_and_does_not_imply_extra_profiles()
    {
        var plan = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.FanOut,
            [
                Candidate("inbox", enabled: true, channel: "inbox"),
                Candidate("sms-primary", enabled: true, channel: "sms"),
                Candidate("sms-disabled", enabled: false, channel: "sms"),
            ]);
        var empty = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.FanOut,
            [Candidate("sms-disabled", enabled: false)]);

        CollectionAssert.AreEqual(
            new[] { "inbox", "sms-primary" },
            plan.Targets.Select(target => target.ProfileKey).ToArray());
        Assert.AreEqual(NotificationsErrorCodes.RouteFanOutEmpty, empty.ErrorCode);
    }

    [TestMethod]
    public void Failover_switches_only_on_transient_or_rate_limit_failures()
    {
        var candidates = new[]
        {
            Candidate("sms-primary", enabled: true, order: 1),
            Candidate("sms-backup", enabled: true, order: 2),
        };

        var first = NotificationRoutePlanner.Plan(NotificationDispatchMode.Failover, candidates);
        var afterTransient = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Failover,
            candidates,
            NotificationFailureCategory.Transient,
            failedProfileKey: "sms-primary");
        var afterPermanent = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Failover,
            candidates,
            NotificationFailureCategory.PermanentContent,
            failedProfileKey: "sms-primary");

        Assert.AreEqual("sms-primary", first.Targets.Single().ProfileKey);
        Assert.AreEqual("sms-backup", afterTransient.Targets.Single().ProfileKey);
        Assert.AreEqual(NotificationsErrorCodes.RouteFailoverPermanent, afterPermanent.ErrorCode);
    }

    [TestMethod]
    public void Match_requires_exactly_one_matching_enabled_profile()
    {
        var unique = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Match,
            [
                Candidate("sms-cn", enabled: true, matches: true),
                Candidate("sms-us", enabled: true, matches: false),
            ]);
        var none = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Match,
            [Candidate("sms-cn", enabled: true, matches: false)]);
        var many = NotificationRoutePlanner.Plan(
            NotificationDispatchMode.Match,
            [
                Candidate("sms-cn", enabled: true, matches: true),
                Candidate("sms-backup", enabled: true, matches: true),
            ]);

        Assert.AreEqual("sms-cn", unique.Targets.Single().ProfileKey);
        Assert.AreEqual(NotificationsErrorCodes.RouteMatchNone, none.ErrorCode);
        Assert.AreEqual(NotificationsErrorCodes.RouteMatchAmbiguous, many.ErrorCode);
    }

    private static NotificationRouteCandidate Candidate(
        string profileKey,
        bool enabled,
        string channel = "sms",
        int order = 1,
        bool matches = true) =>
        new(profileKey, channel, order, enabled, matches);
}
