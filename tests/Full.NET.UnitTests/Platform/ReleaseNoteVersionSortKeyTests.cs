using Full.NET.Modules.Platform.Domain;

namespace Full.NET.UnitTests.Platform;

/// <summary>更新日志版本排序键解析规则测试。</summary>
[TestClass]
public sealed class ReleaseNoteVersionSortKeyTests
{
    [TestMethod]
    public void TryParse_accepts_one_to_four_numeric_segments()
    {
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1", out var one));
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1.2", out var two));
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1.2.3", out var three));
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1.2.3.4", out var four));

        Assert.AreEqual(1_000_000_000L, one);
        Assert.AreEqual(1_002_000_000L, two);
        Assert.AreEqual(1_002_003_000L, three);
        Assert.AreEqual(1_002_003_004L, four);
    }

    [TestMethod]
    public void TryParse_orders_semver_like_labels_descending_by_sort_key()
    {
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1.0.0", out var lower));
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("1.0.1", out var higher));
        Assert.IsTrue(ReleaseNoteVersionSortKey.TryParse("2.0.0", out var major));

        Assert.IsTrue(lower < higher);
        Assert.IsTrue(higher < major);
    }

    [TestMethod]
    public void TryParse_rejects_invalid_labels()
    {
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse(null, out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse(" ", out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse("v1.0.0", out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse("1.2.3.4.5", out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse("1000.0.0", out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse("1.-1.0", out _));
        Assert.IsFalse(ReleaseNoteVersionSortKey.TryParse("1..2", out _));
    }
}
