using Full.NET.Modules.Regions.Contracts;
using Full.NET.Modules.Regions.Domain;

namespace Full.NET.UnitTests.Regions;

[TestClass]
public sealed class DatasetVersionSortKeyTests
{
    [TestMethod]
    public void TryParse_encodes_semver_like_versions()
    {
        Assert.IsTrue(DatasetVersionSortKey.TryParse("1", out var one));
        Assert.AreEqual(1_000_000_000L, one);
        Assert.IsTrue(DatasetVersionSortKey.TryParse("1.2.3", out var parsed));
        Assert.AreEqual(1_002_003_000L, parsed);
    }

    [TestMethod]
    public void TryParse_rejects_invalid_versions()
    {
        Assert.IsFalse(DatasetVersionSortKey.TryParse("", out _));
        Assert.IsFalse(DatasetVersionSortKey.TryParse("1..0", out _));
        Assert.IsFalse(DatasetVersionSortKey.TryParse("1000", out _));
    }
}
