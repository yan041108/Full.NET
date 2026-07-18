using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;

namespace Full.NET.UnitTests.Seeding;

[TestClass]
public sealed class SeedContributorGraphTests
{
    [TestMethod]
    public void Independent_contributors_are_ordered_by_stable_name()
    {
        var ordered = SeedContributorGraph.Order(
            [
                Contributor("tenancy.zone"),
                Contributor("identity.authorization"),
            ],
            SeedProfile.Baseline);

        CollectionAssert.AreEqual(
            new[] { "identity.authorization", "tenancy.zone" },
            ordered.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public void Dependencies_are_executed_before_dependants()
    {
        var ordered = SeedContributorGraph.Order(
            [
                Contributor("identity.host_administrator", "tenancy.host"),
                Contributor("tenancy.host"),
            ],
            SeedProfile.Baseline);

        CollectionAssert.AreEqual(
            new[] { "tenancy.host", "identity.host_administrator" },
            ordered.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    [DataRow(SeedProfile.Development, "tenancy.development")]
    [DataRow(SeedProfile.Demo, "tenancy.demo")]
    [DataRow(SeedProfile.Test, "tenancy.test")]
    public void Overlay_includes_baseline_and_excludes_other_overlays(
        SeedProfile profile,
        string expectedOverlay)
    {
        var ordered = SeedContributorGraph.Order(
            [
                Contributor("tenancy.baseline"),
                Contributor("tenancy.development", profiles: Profile(SeedProfile.Development)),
                Contributor("tenancy.demo", profiles: Profile(SeedProfile.Demo)),
                Contributor("tenancy.test", profiles: Profile(SeedProfile.Test)),
            ],
            profile);

        CollectionAssert.AreEquivalent(
            new[] { "tenancy.baseline", expectedOverlay },
            ordered.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public void Duplicate_names_are_rejected_before_profile_filtering()
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(() =>
            SeedContributorGraph.Order(
                [
                    Contributor("tenancy.local", profiles: Profile(SeedProfile.Development)),
                    Contributor("tenancy.local", profiles: Profile(SeedProfile.Demo)),
                ],
                SeedProfile.Baseline));

        Assert.AreEqual(SeedErrorCodes.ContributorDuplicate, exception.Code);
    }

    [TestMethod]
    [DataRow("Tenancy.host", 1, SeedErrorCodes.ContributorNameInvalid)]
    [DataRow("tenancy.host", 0, SeedErrorCodes.ContributorVersionInvalid)]
    public void Invalid_contributor_contract_is_rejected(
        string name,
        int version,
        string expectedCode)
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(() =>
            SeedContributorGraph.Order(
                [Contributor(name, version: version)],
                SeedProfile.Baseline));

        Assert.AreEqual(expectedCode, exception.Code);
    }

    [TestMethod]
    public void Missing_dependency_is_rejected()
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(() =>
            SeedContributorGraph.Order(
                [Contributor("identity.host_administrator", "tenancy.host")],
                SeedProfile.Baseline));

        Assert.AreEqual(SeedErrorCodes.DependencyMissing, exception.Code);
    }

    [TestMethod]
    public void Dependency_in_another_overlay_is_not_available()
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(() =>
            SeedContributorGraph.Order(
                [
                    Contributor(
                        "identity.development",
                        "tenancy.demo",
                        profiles: Profile(SeedProfile.Development)),
                    Contributor("tenancy.demo", profiles: Profile(SeedProfile.Demo)),
                ],
                SeedProfile.Development));

        Assert.AreEqual(SeedErrorCodes.DependencyMissing, exception.Code);
    }

    [TestMethod]
    public void Dependency_cycle_is_rejected()
    {
        var exception = Assert.ThrowsExactly<SeedConfigurationException>(() =>
            SeedContributorGraph.Order(
                [
                    Contributor("identity.authorization", "tenancy.host"),
                    Contributor("tenancy.host", "identity.authorization"),
                ],
                SeedProfile.Baseline));

        Assert.AreEqual(SeedErrorCodes.DependencyCycle, exception.Code);
    }

    private static StubContributor Contributor(
        string name,
        string? dependency = null,
        int version = 1,
        IReadOnlySet<SeedProfile>? profiles = null) =>
        new(
            name,
            version,
            profiles ?? new HashSet<SeedProfile> { SeedProfile.Baseline },
            dependency is null ? [] : [dependency]);

    private static IReadOnlySet<SeedProfile> Profile(SeedProfile profile) =>
        new HashSet<SeedProfile> { profile };

    private sealed class StubContributor(
        string name,
        int version,
        IReadOnlySet<SeedProfile> profiles,
        IReadOnlyCollection<string> dependencies) : IDataSeedContributor
    {
        public string Name { get; } = name;

        public int Version { get; } = version;

        public IReadOnlySet<SeedProfile> Profiles { get; } = profiles;

        public IReadOnlyCollection<string> Dependencies { get; } = dependencies;

        public Task<SeedContributionResult> SeedAsync(
            SeedContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
