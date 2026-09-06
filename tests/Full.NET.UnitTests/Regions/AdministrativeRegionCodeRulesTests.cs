using Full.NET.Modules.Regions.Domain;

namespace Full.NET.UnitTests.Regions;

[TestClass]
public sealed class AdministrativeRegionCodeRulesTests
{
    [TestMethod]
    public void TryNormalize_accepts_numeric_codes_up_to_12_digits()
    {
        Assert.IsTrue(AdministrativeRegionCodeRules.TryNormalize("110000", out var code));
        Assert.AreEqual("110000", code);
        Assert.IsTrue(AdministrativeRegionCodeRules.TryNormalize("1", out _));
    }

    [TestMethod]
    public void TryNormalize_rejects_non_numeric_or_too_long_codes()
    {
        Assert.IsFalse(AdministrativeRegionCodeRules.TryNormalize("ABC", out _));
        Assert.IsFalse(AdministrativeRegionCodeRules.TryNormalize("1234567890123", out _));
        Assert.IsFalse(AdministrativeRegionCodeRules.TryNormalize("  ", out _));
    }

    [TestMethod]
    public void IsValidLevel_only_allows_1_to_5()
    {
        Assert.IsTrue(AdministrativeRegionCodeRules.IsValidLevel(1));
        Assert.IsTrue(AdministrativeRegionCodeRules.IsValidLevel(5));
        Assert.IsFalse(AdministrativeRegionCodeRules.IsValidLevel(0));
        Assert.IsFalse(AdministrativeRegionCodeRules.IsValidLevel(6));
    }
}
