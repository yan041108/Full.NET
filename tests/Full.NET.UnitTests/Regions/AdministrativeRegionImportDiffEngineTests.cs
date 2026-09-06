using Full.NET.Modules.Regions.Contracts;
using Full.NET.Modules.Regions.Domain;

namespace Full.NET.UnitTests.Regions;

[TestClass]
public sealed class AdministrativeRegionImportDiffEngineTests
{
    [TestMethod]
    public void Compute_reports_added_updated_removed_and_skipped()
    {
        var existing = new Dictionary<string, AdministrativeRegionDiffSnapshot>(StringComparer.Ordinal)
        {
            ["110000"] = new(
                "110000",
                "北京市",
                "北京",
                "中国,北京市",
                null,
                "010",
                1,
                "直辖市",
                null,
                null,
                null,
                10,
                null),
            ["440000"] = new(
                "440000",
                "广东省",
                "广东",
                "中国,广东省",
                null,
                null,
                1,
                "省",
                null,
                null,
                null,
                20,
                null),
        };

        var incoming = new[]
        {
            new AdministrativeRegionDiffSnapshot(
                "110000",
                "北京市",
                "北京",
                "中国,北京市,直辖市",
                null,
                "010",
                1,
                "直辖市",
                null,
                null,
                null,
                10,
                null),
            new AdministrativeRegionDiffSnapshot(
                "440100",
                "广州市",
                "广州",
                "中国,广东省,广州市",
                null,
                "020",
                2,
                null,
                null,
                null,
                null,
                10,
                "440000"),
        };

        var preview = AdministrativeRegionImportDiffEngine.Compute(
            existing,
            incoming,
            AdministrativeRegionImportMergeModes.Replace,
            skippedCount: 2);

        Assert.HasCount(1, preview.Added);
        Assert.AreEqual("440100", preview.Added[0].Code);
        Assert.HasCount(1, preview.Updated);
        Assert.AreEqual("mergerName", preview.Updated[0].ChangedFields[0]);
        Assert.HasCount(1, preview.Removed);
        Assert.AreEqual("440000", preview.Removed[0].Code);
        Assert.AreEqual(2, preview.SkippedCount);
    }

    [TestMethod]
    public void Compute_merge_mode_does_not_report_removed()
    {
        var existing = new Dictionary<string, AdministrativeRegionDiffSnapshot>(StringComparer.Ordinal)
        {
            ["440000"] = new(
                "440000",
                "广东省",
                null,
                null,
                null,
                null,
                1,
                null,
                null,
                null,
                null,
                0,
                null),
        };

        var preview = AdministrativeRegionImportDiffEngine.Compute(
            existing,
            Array.Empty<AdministrativeRegionDiffSnapshot>(),
            AdministrativeRegionImportMergeModes.Merge,
            skippedCount: 0);

        Assert.HasCount(0, preview.Removed);
    }
}
