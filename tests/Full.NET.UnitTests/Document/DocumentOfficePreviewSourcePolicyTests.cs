using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Domain;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentOfficePreviewSourcePolicyTests
{
    [TestMethod]
    public void IsOfficeMime_accepts_docx_and_legacy_word()
    {
        Assert.IsTrue(DocumentOfficePreviewSourcePolicy.IsOfficeMime(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        Assert.IsTrue(DocumentOfficePreviewSourcePolicy.IsOfficeMime("application/msword; charset=utf-8"));
    }

    [TestMethod]
    public void IsOfficeMime_rejects_pdf_and_plain_text()
    {
        Assert.IsFalse(DocumentOfficePreviewSourcePolicy.IsOfficeMime("application/pdf"));
        Assert.IsFalse(DocumentOfficePreviewSourcePolicy.IsOfficeMime("text/plain"));
        Assert.IsFalse(DocumentOfficePreviewSourcePolicy.IsOfficeMime(null));
    }
}
