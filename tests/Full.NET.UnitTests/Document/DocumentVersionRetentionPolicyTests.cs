using Full.NET.Modules.Document.Domain;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentVersionRetentionPolicyTests
{
    [TestMethod]
    public void CanDeleteVersion_requires_more_than_minimum_retained_versions()
    {
        Assert.IsFalse(DocumentVersionRetentionPolicy.CanDeleteVersion(1, 1));
        Assert.IsFalse(DocumentVersionRetentionPolicy.CanDeleteVersion(2, 2));
        Assert.IsTrue(DocumentVersionRetentionPolicy.CanDeleteVersion(3, 2));
    }

    [TestMethod]
    public void CountExcessHistoryVersions_returns_zero_when_retention_disabled()
    {
        Assert.AreEqual(0, DocumentVersionRetentionPolicy.CountExcessHistoryVersions(10, 0));
        Assert.AreEqual(0, DocumentVersionRetentionPolicy.CountExcessHistoryVersions(10, -1));
    }

    [TestMethod]
    public void CountExcessHistoryVersions_excludes_current_version_from_history_budget()
    {
        Assert.AreEqual(0, DocumentVersionRetentionPolicy.CountExcessHistoryVersions(2, 1));
        Assert.AreEqual(1, DocumentVersionRetentionPolicy.CountExcessHistoryVersions(3, 1));
        Assert.AreEqual(2, DocumentVersionRetentionPolicy.CountExcessHistoryVersions(4, 1));
    }
}
