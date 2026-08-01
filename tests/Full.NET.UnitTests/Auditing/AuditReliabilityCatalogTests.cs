using Full.NET.Abstractions.Auditing;
using Full.NET.Modules.Auditing;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditReliabilityCatalogTests
{
    [TestMethod]
    public void CreateDefault_classifies_the_well_known_tenancy_disable_action_as_domain_transactional()
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        Assert.AreEqual(
            AuditReliabilityClass.DomainTransactional,
            catalog.GetRequired("tenancy.host_tenant.disable"));
    }

    [TestMethod]
    public void CreateDefault_classifies_operation_log_writes_as_important_http()
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        Assert.AreEqual(
            AuditReliabilityClass.ImportantHttp,
            catalog.GetRequired("auditing.operation_log.write"));
    }

    [TestMethod]
    public void CreateDefault_classifies_access_log_writes_as_best_effort()
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        Assert.AreEqual(
            AuditReliabilityClass.BestEffort,
            catalog.GetRequired("auditing.access_log.write"));
    }

    [TestMethod]
    public void GetRequired_throws_for_an_unknown_action_key_instead_of_defaulting()
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => catalog.GetRequired("tenancy.unregistered_action.never_declared"));
        StringAssert.Contains(
            exception.Message,
            "tenancy.unregistered_action.never_declared");
    }

    [TestMethod]
    public void GetRequired_rejects_a_null_action_key()
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => catalog.GetRequired(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void GetRequired_rejects_a_blank_action_key(string actionKey)
    {
        var catalog = AuditReliabilityCatalog.CreateDefault();

        Assert.ThrowsExactly<ArgumentException>(
            () => catalog.GetRequired(actionKey));
    }

    [TestMethod]
    public void Constructor_throws_when_two_entries_declare_the_same_action_key()
    {
        var duplicateEntries = new[]
        {
            new AuditReliabilityCatalogEntry(
                "tenancy.host_tenant.disable",
                AuditReliabilityClass.DomainTransactional),
            new AuditReliabilityCatalogEntry(
                "tenancy.host_tenant.disable",
                AuditReliabilityClass.BestEffort),
        };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = new AuditReliabilityCatalog(duplicateEntries));
        StringAssert.Contains(
            exception.Message,
            "tenancy.host_tenant.disable");
    }
}
