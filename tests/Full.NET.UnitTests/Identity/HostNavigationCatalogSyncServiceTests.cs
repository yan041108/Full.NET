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

    [TestMethod]
    public void BuildDomainDirectoryRouteName_PrefixesDomainKey()
    {
        Assert.AreEqual(
            "domain-overview-platform",
            HostNavigationCatalogSyncService.BuildDomainDirectoryRouteName("overview-platform"));
    }

    [TestMethod]
    public void BuildDomainDirectoryPath_UsesDomainsSegment()
    {
        Assert.AreEqual(
            "/domains/tenancy-commerce",
            HostNavigationCatalogSyncService.BuildDomainDirectoryPath("tenancy-commerce"));
    }

    [TestMethod]
    public void IsDomainDirectoryRouteName_DetectsDomainDirectories()
    {
        Assert.IsTrue(
            HostNavigationCatalogSyncService.IsDomainDirectoryRouteName("domain-other"));
        Assert.IsFalse(
            HostNavigationCatalogSyncService.IsDomainDirectoryRouteName("module-identity"));
    }

    [TestMethod]
    public void ResolveDomainKeyForModule_MapsKnownModulesAndFallsBackToOther()
    {
        Assert.AreEqual(
            "overview-platform",
            HostNavigationDomainLayout.ResolveDomainKeyForModule("identity"));
        Assert.AreEqual(
            "operations-security",
            HostNavigationDomainLayout.ResolveDomainKeyForModule("settings"));
        Assert.AreEqual(
            HostNavigationDomainLayout.OtherDomainKey,
            HostNavigationDomainLayout.ResolveDomainKeyForModule("unknown-module"));
    }
}
