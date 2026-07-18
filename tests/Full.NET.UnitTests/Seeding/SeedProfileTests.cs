using Full.NET.Seeding.Abstractions;

namespace Full.NET.UnitTests.Seeding;

[TestClass]
public sealed class SeedProfileTests
{
    [TestMethod]
    [DataRow("baseline", SeedProfile.Baseline)]
    [DataRow("development", SeedProfile.Development)]
    [DataRow("DEVELOPMENT", SeedProfile.Development)]
    [DataRow("demo", SeedProfile.Demo)]
    [DataRow("test", SeedProfile.Test)]
    public void Supported_names_are_parsed_exactly(
        string value,
        SeedProfile expected)
    {
        Assert.IsTrue(SeedProfileNames.TryParse(value, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("production")]
    [DataRow("dev")]
    public void Unsupported_names_are_rejected(string? value) =>
        Assert.IsFalse(SeedProfileNames.TryParse(value, out _));

    [TestMethod]
    [DataRow(SeedProfile.Baseline, 1)]
    [DataRow(SeedProfile.Development, 2)]
    [DataRow(SeedProfile.Demo, 2)]
    [DataRow(SeedProfile.Test, 2)]
    public void Overlay_profiles_include_baseline_and_only_their_own_layer(
        SeedProfile profile,
        int expectedCount)
    {
        var layers = profile.EffectiveLayers();

        Assert.HasCount(expectedCount, layers);
        CollectionAssert.Contains(layers.ToArray(), SeedProfile.Baseline);
        if (profile != SeedProfile.Baseline)
        {
            CollectionAssert.Contains(layers.ToArray(), profile);
        }
    }
}
