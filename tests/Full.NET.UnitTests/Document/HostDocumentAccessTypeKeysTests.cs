using Full.NET.Modules.Document.Contracts;

namespace Full.NET.UnitTests.Document;

/// <summary>单元测试（文档）：Host Document Access Type Keys。</summary>
[TestClass]
public sealed class HostDocumentAccessTypeKeysTests
{
    [TestMethod]
    public void All_contains_expected_access_types()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                HostDocumentAccessTypeKeys.Download,
                HostDocumentAccessTypeKeys.Preview,
                HostDocumentAccessTypeKeys.ShareAccess,
            },
            HostDocumentAccessTypeKeys.All.ToArray());
    }
}
