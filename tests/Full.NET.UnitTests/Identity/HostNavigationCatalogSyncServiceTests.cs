using Full.NET.Modules.Identity.Features.ManageHostMenus;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostNavigationCatalogSyncServiceTests
{
    [TestMethod]
    public void BuildActionRouteName_ReplacesDotsWithHyphens()
    {
        Assert.AreEqual(
            "identity-users-create",
            HostNavigationCatalogSyncService.BuildActionRouteName("identity.users.create"));
    }

    [TestMethod]
    public void BuildModuleDirectoryRouteName_PrefixesModuleKey()
    {
        Assert.AreEqual(
            "module-identity",
            HostNavigationCatalogSyncService.BuildModuleDirectoryRouteName("identity"));
    }

    [TestMethod]
    public void BuildModuleDirectoryPath_UsesModulesSegment()
    {
        Assert.AreEqual(
            "/modules/tenancy",
            HostNavigationCatalogSyncService.BuildModuleDirectoryPath("tenancy"));
    }

    [TestMethod]
    public void IsModuleDirectoryRouteName_DetectsModuleDirectories()
    {
        Assert.IsTrue(
            HostNavigationCatalogSyncService.IsModuleDirectoryRouteName("module-identity"));
        Assert.IsFalse(
            HostNavigationCatalogSyncService.IsModuleDirectoryRouteName("users"));
    }
}
