using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class PaymentMerchantMaskingTests
{
    [TestMethod]
    public void MaskIdentifier_hides_middle_segment()
    {
        var masked = PaymentMerchantMasking.MaskIdentifier("wx1234567890abcdef");

        Assert.Contains("***", masked);
        Assert.DoesNotContain("345678", masked);
    }

    [TestMethod]
    public void MaskNotifyUrl_hides_host_middle_segment()
    {
        var masked = PaymentMerchantMasking.MaskNotifyUrl("https://pay.example.com/api/v1/callbacks/wechat");

        Assert.Contains("***", masked);
        Assert.DoesNotContain("example", masked);
    }
}
