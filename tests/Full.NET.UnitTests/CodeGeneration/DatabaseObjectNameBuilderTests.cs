using System.Globalization;
using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class DatabaseObjectNameBuilderTests
{
    [TestMethod]
    public void Build_preserves_short_name()
    {
        Assert.AreEqual(
            "PK_fn_identity_user",
            DatabaseObjectNameBuilder.Build("PK_fn_identity_user"));
    }

    [TestMethod]
    public void Build_compresses_long_name_with_shared_digest()
    {
        Assert.AreEqual(
            "IX_fn_notifications_delivery_attempt_SubscriptionId_Req_5b137a8d",
            DatabaseObjectNameBuilder.Build(
                "IX_fn_notifications_delivery_attempt_SubscriptionId_RequestedAtUtc_ChannelProvider"));
    }

    [TestMethod]
    public void Build_rejects_unknown_prefix_and_non_ascii_name()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => DatabaseObjectNameBuilder.Build("TABLE_fn_identity_user"));
        Assert.ThrowsExactly<ArgumentException>(
            () => DatabaseObjectNameBuilder.Build("IX_fn_identity_用户"));
    }

    [TestMethod]
    public void Build_is_independent_from_current_culture()
    {
        const string input =
            "IX_fn_notifications_delivery_attempt_SubscriptionId_RequestedAtUtc_ChannelProvider";
        using var culture = new CultureScope("tr-TR");

        Assert.AreEqual(
            "IX_fn_notifications_delivery_attempt_SubscriptionId_Req_5b137a8d",
            DatabaseObjectNameBuilder.Build(input));
    }

    [TestMethod]
    public void Build_has_no_collision_in_fixed_one_hundred_thousand_sample()
    {
        var names = Enumerable.Range(0, 100_000)
            .Select(index => DatabaseObjectNameBuilder.Build(
                $"IX_fn_identity_user_Column{index:D6}_RepeatedLongSuffixForDeterminism"))
            .ToArray();

        Assert.AreEqual(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
