using Full.NET.Modules.Organization.TenantUnits;

namespace Full.NET.UnitTests.Organization;

/// <summary>验证机构上级链解析的层级边界与环检测。</summary>
[TestClass]
public sealed class OrganizationUnitAncestryRulesTests
{
    /// <summary>沿有效上级链应解析到目标层级机构。</summary>
    [TestMethod]
    public void ResolveAncestorUnitId_walks_up_the_parent_chain()
    {
        var root = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var lookup = new Dictionary<Guid, Guid?>
        {
            [child] = parent,
            [parent] = root,
            [root] = null,
        };

        var levelOne = OrganizationUnitAncestryRules.ResolveAncestorUnitId(
            child,
            1,
            unitId => lookup[unitId]);
        var levelTwo = OrganizationUnitAncestryRules.ResolveAncestorUnitId(
            child,
            2,
            unitId => lookup[unitId]);

        Assert.AreEqual(parent, levelOne);
        Assert.AreEqual(root, levelTwo);
    }

    /// <summary>层级不足或检测到组织环时必须失败关闭。</summary>
    [TestMethod]
    public void ResolveAncestorUnitId_rejects_missing_parent_and_cycles()
    {
        var unitA = Guid.CreateVersion7();
        var unitB = Guid.CreateVersion7();
        var cyclicLookup = new Dictionary<Guid, Guid?>
        {
            [unitA] = unitB,
            [unitB] = unitA,
        };

        Assert.IsNull(OrganizationUnitAncestryRules.ResolveAncestorUnitId(
            unitA,
            2,
            unitId => cyclicLookup[unitId]));
        Assert.IsNull(OrganizationUnitAncestryRules.ResolveAncestorUnitId(
            unitA,
            2,
            _ => null));
        Assert.IsFalse(OrganizationUnitAncestryRules.IsValidAncestorLevel(0));
        Assert.IsFalse(OrganizationUnitAncestryRules.IsValidAncestorLevel(21));
    }
}
