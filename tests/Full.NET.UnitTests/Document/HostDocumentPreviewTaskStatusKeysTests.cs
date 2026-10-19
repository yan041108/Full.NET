using Full.NET.Modules.Document.Contracts;

namespace Full.NET.UnitTests.Document;

/// <summary>单元测试（文档）：Host Document Preview Task Status Keys。</summary>
[TestClass]
public sealed class HostDocumentPreviewTaskStatusKeysTests
{
    [TestMethod]
    public void All_contains_expected_status_keys()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                HostDocumentPreviewTaskStatusKeys.Pending,
                HostDocumentPreviewTaskStatusKeys.Processing,
                HostDocumentPreviewTaskStatusKeys.Succeeded,
                HostDocumentPreviewTaskStatusKeys.Failed,
            },
            HostDocumentPreviewTaskStatusKeys.All.ToArray());
    }
}
