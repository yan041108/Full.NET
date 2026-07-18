using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;

namespace Full.NET.UnitTests.Seeding;

[TestClass]
public sealed class SeedCommandLineTests
{
    [TestMethod]
    public void Missing_seed_arguments_disable_seeding()
    {
        var options = SeedCommandLine.Parse([]);

        Assert.IsNull(options.Profile);
        Assert.IsFalse(options.UsesLegacyAlias);
    }

    [TestMethod]
    [DataRow("baseline", SeedProfile.Baseline)]
    [DataRow("development", SeedProfile.Development)]
    [DataRow("demo", SeedProfile.Demo)]
    [DataRow("test", SeedProfile.Test)]
    public void Explicit_seed_argument_selects_supported_profile(
        string value,
        SeedProfile expected)
    {
        var options = SeedCommandLine.Parse(["--seed", value]);

        Assert.AreEqual(expected, options.Profile);
        Assert.IsFalse(options.UsesLegacyAlias);
    }

    [TestMethod]
    public void Legacy_alias_maps_to_development_profile()
    {
        var options = SeedCommandLine.Parse(["--seed-local"]);

        Assert.AreEqual(SeedProfile.Development, options.Profile);
        Assert.IsTrue(options.UsesLegacyAlias);
    }

    [TestMethod]
    [DataRow("--seed")]
    [DataRow("--seed=development")]
    [DataRow("--seed", "production")]
    [DataRow("--seed", "development", "--seed", "demo")]
    [DataRow("--seed-local", "--seed", "development")]
    [DataRow("--seed-local", "--seed-local")]
    public void Invalid_seed_arguments_are_rejected(params string[] arguments)
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(
            () => SeedCommandLine.Parse(arguments));

        Assert.AreEqual(SeedErrorCodes.CommandInvalid, exception.Code);
        Assert.AreEqual(exception.Code, exception.Message);
    }

    [TestMethod]
    public void Unrelated_host_arguments_are_ignored()
    {
        var options = SeedCommandLine.Parse(
            ["--environment", "Development", "--seed", "baseline"]);

        Assert.AreEqual(SeedProfile.Baseline, options.Profile);
    }
}
