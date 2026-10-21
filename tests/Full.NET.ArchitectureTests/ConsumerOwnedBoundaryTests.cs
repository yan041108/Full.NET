using Full.NET.Composition;
using Full.NET.Modules.DataApproval;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>锁定消费方端口所有权和官方发布闭包，防止静态依赖与运行时依赖再次倒置。</summary>
[TestClass]
public sealed class ConsumerOwnedBoundaryTests
{
    [TestMethod]
    public void Identity_delivery_port_is_owned_by_identity_without_notifications_reverse_reference()
    {
        Assert.AreEqual(typeof(VerifiedTenantContext).Assembly, typeof(IIdentityChallengeDeliveryPort).Assembly);
        Assert.IsFalse(typeof(IdentityModule).Assembly.GetReferencedAssemblies()
            .Any(reference => reference.Name == "Full.NET.Modules.Notifications.Contracts"));
    }

    [TestMethod]
    public void Approval_orchestration_has_no_serial_number_assembly_dependency()
    {
        Assert.IsFalse(typeof(DataApprovalModule).Assembly.GetReferencedAssemblies()
            .Any(reference => reference.Name?.StartsWith("Full.NET.Modules.SerialNumbers", StringComparison.Ordinal) == true));
        Assert.IsFalse(((Full.NET.Modularity.Modules.IFullNetModule)new DataApprovalModule()).OptionalContractDependencies.Contains("SerialNumbers"));
    }

    [TestMethod]
    public void Official_composition_and_presets_exclude_enterprise_sample()
    {
        Assert.IsFalse(typeof(FullNetModuleCatalog).Assembly.GetReferencedAssemblies()
            .Any(reference => reference.Name == "Full.NET.Modules.EnterpriseRequest"));
        Assert.IsFalse(FullNetModuleSelection.OfficialModuleNames.Contains("EnterpriseRequest"));
        Assert.IsFalse(FullNetModuleSelection.EnterprisePresetModuleNames.Contains("EnterpriseRequest"));
    }

    [TestMethod]
    public void Official_migrator_does_not_own_sample_schema()
    {
        Assert.IsFalse(typeof(Full.NET.Migrations.DbUp.IDatabaseMigrationRunner).Assembly.GetManifestResourceNames()
            .Any(name => name.Contains("DemoEnterpriseRequest", StringComparison.Ordinal)));
        Assert.HasCount(2, typeof(Full.NET.Modules.EnterpriseRequest.EnterpriseRequestModule).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith("001_EnterpriseRequest.sql", StringComparison.Ordinal)).ToArray());
    }
}
