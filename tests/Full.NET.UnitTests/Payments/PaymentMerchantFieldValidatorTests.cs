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
            "https://pay.example.com/api/v1/payments/callbacks/wechat",
            string.Empty);

        Assert.IsNull(message);
    }

    [TestMethod]
    public void ValidateMetadata_accepts_alipay_page_with_return_url()
    {
        var message = PaymentMerchantFieldValidator.ValidateMetadata(
            "Alipay Page",
            PaymentChannelKeys.AlipayPage,
            "2021000123456789",
            string.Empty,
            string.Empty,
            "https://pay.example.com/api/v1/payments/callbacks/alipay",
            "https://pay.example.com/payments/return");

        Assert.IsNull(message);
    }

    [TestMethod]
    public void ValidateMetadata_rejects_alipay_page_without_return_url()
    {
        var message = PaymentMerchantFieldValidator.ValidateMetadata(
            "Alipay Page",
            PaymentChannelKeys.AlipayPage,
            "2021000123456789",
            string.Empty,
            string.Empty,
            "https://pay.example.com/api/v1/payments/callbacks/alipay",
            string.Empty);

        Assert.IsNotNull(message);
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
            "http://pay.example.com/callback",
            string.Empty);

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

    [TestMethod]
    public void ValidateOrderRequest_accepts_alipay_page_channel_key()
    {
        var message = PaymentMerchantFieldValidator.ValidateOrderRequest(
            100,
            PaymentMerchantFieldValidator.DefaultCurrency,
            "Test product",
            null,
            PaymentChannelKeys.AlipayPage);

        Assert.IsNull(message);
    }
}
