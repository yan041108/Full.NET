using Full.NET.Modules.Regions.Domain;

namespace Full.NET.UnitTests.Regions;

[TestClass]
public sealed class AdministrativeRegionTreeRulesTests
{
    [TestMethod]
    public void WouldCreateParentCycle_detects_direct_and_transitive_cycles()
    {
        var links = new[]
        {
            new AdministrativeRegionParentLink(Guid.Parse("11111111-1111-1111-1111-111111111101"), null),
            new AdministrativeRegionParentLink(Guid.Parse("11111111-1111-1111-1111-111111111102"), Guid.Parse("11111111-1111-1111-1111-111111111101")),
            new AdministrativeRegionParentLink(Guid.Parse("11111111-1111-1111-1111-111111111103"), Guid.Parse("11111111-1111-1111-1111-111111111102")),
        };

        Assert.IsTrue(AdministrativeRegionTreeRules.WouldCreateParentCycle(
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            Guid.Parse("11111111-1111-1111-1111-111111111103"),
            links));
        Assert.IsFalse(AdministrativeRegionTreeRules.WouldCreateParentCycle(
            Guid.Parse("11111111-1111-1111-1111-111111111103"),
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            links));
    }

    [TestMethod]
    public void ImportWouldCreateCycle_detects_parent_code_cycles()
    {
        var items = new Dictionary<string, ImportRegionItemSnapshot>(StringComparer.Ordinal)
        {
            ["110000"] = new("110000", "110100"),
            ["110100"] = new("110100", "110000"),
        };

        Assert.IsTrue(AdministrativeRegionTreeRules.ImportWouldCreateCycle(items));
    }
}
