using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationIntentAttachmentRulesTests
{
    [TestMethod]
    public void NormalizeFileIds_deduplicates_invalid_when_duplicate_present()
    {
        var first = Guid.Parse("01912345-6789-7abc-8def-0123456789ab");
        var normalized = NotificationIntentAttachmentRules.NormalizeFileIds([first, first]);
        Assert.AreEqual(0, normalized.Count);
    }

    [TestMethod]
    public void NormalizeFileIds_preserves_order_for_unique_ids()
    {
        var first = Guid.Parse("01912345-6789-7abc-8def-0123456789ab");
        var second = Guid.Parse("01912345-6789-7abc-8def-0123456789ac");
        var normalized = NotificationIntentAttachmentRules.NormalizeFileIds([first, second]);
        CollectionAssert.AreEqual(new[] { first, second }, normalized.ToArray());
    }

    [TestMethod]
    public void MatchesAllowedExtension_accepts_pdf_and_rejects_exe()
    {
        Assert.IsTrue(NotificationIntentAttachmentRules.MatchesAllowedExtension("report.pdf"));
        Assert.IsFalse(NotificationIntentAttachmentRules.MatchesAllowedExtension("virus.exe"));
    }
}
