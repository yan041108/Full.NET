using Full.NET.Modules.Ocr.Domain;

namespace Full.NET.UnitTests.Ocr;

[TestClass]
public sealed class OcrIdCardMaskingTests
{
    [TestMethod]
    public void MaskIdNumber_masks_middle_digits_for_long_numbers()
    {
        var masked = OcrIdCardMasking.MaskIdNumber("110101199001011234");
        Assert.AreEqual("110***********1234", masked);
    }

    [TestMethod]
    public void MaskIdNumber_returns_all_asterisks_for_short_numbers()
    {
        var masked = OcrIdCardMasking.MaskIdNumber("12345678");
        Assert.AreEqual("********", masked);
    }

    [TestMethod]
    public void MaskIdNumber_returns_null_for_null_input()
    {
        Assert.IsNull(OcrIdCardMasking.MaskIdNumber(null));
    }
}
