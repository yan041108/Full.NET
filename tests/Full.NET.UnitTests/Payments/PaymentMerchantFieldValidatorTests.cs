using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class PaymentMerchantFieldValidatorTests
{
    [TestMethod]
    public void ValidateMetadata_accepts_wechat_native_defaults()
    {
        var message = PaymentMerchantFieldValidator.ValidateMetadata(
            "WeChat Native",
            PaymentChannelKeys.WeChatNative,
            "wx1234567890abcdef",
            "1900000109",
            "7132D72A03E93CDDF8C03BBD1F3700AD9091D",
            "https://pay.example.com/api/v1/payments/callbacks/wechat");

        Assert.IsNull(message);
    }

    [TestMethod]
    public void ValidateMetadata_rejects_notify_url_without_https()
    {
        var message = PaymentMerchantFieldValidator.ValidateMetadata(
            "WeChat Native",
            PaymentChannelKeys.WeChatNative,
            "wx1234567890abcdef",
            "1900000109",
            "7132D72A03E93CDDF8C03BBD1F3700AD9091D",
            "http://pay.example.com/callback");

        Assert.IsNotNull(message);
    }

    [TestMethod]
    public void ValidateOrderRequest_rejects_non_positive_amount()
    {
        var message = PaymentMerchantFieldValidator.ValidateOrderRequest(
            0,
            PaymentMerchantFieldValidator.DefaultCurrency,
            "Test product",
            null);

        Assert.IsNotNull(message);
    }
}
