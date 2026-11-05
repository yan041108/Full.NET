using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Payments;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Serialization;

namespace Full.NET.UnitTests.Payments;

/// <summary>HTTP 金额完整保留 Int64，内部数值及可空全额退款不引入数字线格式兼容层。</summary>
[TestClass]
public sealed class PaymentHttpWirePrecisionTests
{
    private static readonly PaymentsJsonSerializerContext Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static CreatePaymentOrderRequest Order(long amount) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        PaymentChannelKeys.WeChatNative, amount, "CNY", "Original", null, Guid.CreateVersion7());

    [TestMethod]
    [DataRow(100L, "a209b8b96529a9924611271f1d81c53ac9574eb4dace4ea92ecdf7b41c6829f9")]
    [DataRow(long.MaxValue, "15c0c94f6709ae48704cf09485ce2025e3b584a88cf91e484bb44db69fd929a8")]
    public void Creation_identity_keeps_numeric_baseline_digest_independent_of_http(long amount, string expected)
    {
        var request = new CreatePaymentOrderRequest(Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf201"), null, null,
            amount, "CNY", "subject", null, Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf202"));
        Assert.AreEqual(expected, PaymentOrderCreationIdentity.CreateHash(request));
    }

    [TestMethod] [DataRow(9_007_199_254_740_993L)] [DataRow(long.MaxValue)]
    public void Requests_round_trip_exact_amount_and_preserve_null_full_refund(long amount)
    {
        var request = Order(amount); var text = JsonSerializer.Serialize(request, Json.CreatePaymentOrderRequest);
        using var document = JsonDocument.Parse(text);
        Assert.AreEqual(amount.ToString(System.Globalization.CultureInfo.InvariantCulture), document.RootElement.GetProperty("amountMinor").GetString());
        Assert.AreEqual(amount, JsonSerializer.Deserialize(text, Json.CreatePaymentOrderRequest)!.AmountMinor);
        var refund = new CreatePaymentRefundRequest(amount, "original");
        var body = JsonSerializer.Serialize(refund, Json.CreatePaymentRefundRequest);
        Assert.AreEqual(amount, JsonSerializer.Deserialize(body, Json.CreatePaymentRefundRequest)!.AmountMinor);
        var full = JsonSerializer.Serialize(new CreatePaymentRefundRequest(null, "original"), Json.CreatePaymentRefundRequest);
        Assert.IsNull(JsonSerializer.Deserialize(full, Json.CreatePaymentRefundRequest)!.AmountMinor);
        using var fullDocument = JsonDocument.Parse(full);
        Assert.AreEqual(JsonValueKind.Null, fullDocument.RootElement.GetProperty("amountMinor").ValueKind);
    }

    [TestMethod] [DataRow("29")] [DataRow("\"01\"")] [DataRow("\"1e3\"")]
    [DataRow("\"9223372036854775808\"")] [DataRow("null")]
    public void Numeric_noncanonical_or_overflow_order_amount_is_rejected(string invalid)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Order(29), Json.CreatePaymentOrderRequest))!;
        node["amountMinor"] = JsonNode.Parse(invalid);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(node.ToJsonString(), Json.CreatePaymentOrderRequest));
        if (invalid == "null") return;
        var refund = JsonNode.Parse(JsonSerializer.Serialize(new CreatePaymentRefundRequest(29, "original"), Json.CreatePaymentRefundRequest))!;
        refund["amountMinor"] = JsonNode.Parse(invalid);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(refund.ToJsonString(), Json.CreatePaymentRefundRequest));
    }

    [TestMethod]
    public void Order_refund_list_detail_and_nested_checkout_keep_exact_wire_amount()
    {
        var order = new PaymentOrderRecord { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), MerchantConfigId = Guid.CreateVersion7(),
            AmountMinor = long.MaxValue, Currency = "CNY", CreatedAtUtc = DateTimeOffset.UtcNow };
        var refund = new PaymentRefundRecord { Id = Guid.CreateVersion7(), OrderId = order.Id, TenantId = order.TenantId,
            MerchantConfigId = order.MerchantConfigId, AmountMinor = 9_007_199_254_740_993L, Currency = "CNY", CreatedAtUtc = order.CreatedAtUtc };
        AssertAmount(JsonSerializer.Serialize(PaymentOrderMapper.MapDetail(order), Json.PaymentOrderResponse), "9223372036854775807");
        AssertAmount(JsonSerializer.Serialize(PaymentOrderMapper.MapListItem(order), Json.PaymentOrderListItem), "9223372036854775807");
        AssertAmount(JsonSerializer.Serialize(PaymentRefundMapper.MapDetail(refund), Json.PaymentRefundResponse), "9007199254740993");
        AssertAmount(JsonSerializer.Serialize(PaymentRefundMapper.MapListItem(refund), Json.PaymentRefundListItem), "9007199254740993");
        var checkout = new SubscriptionCheckoutResponse(Guid.CreateVersion7(), PaymentOrderMapper.MapDetail(order), "payment_pending", null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(checkout, Json.SubscriptionCheckoutResponse));
        Assert.AreEqual("9223372036854775807", document.RootElement.GetProperty("order").GetProperty("amountMinor").GetString());
        Assert.AreEqual(long.MaxValue, order.AmountMinor);
    }

    [TestMethod]
    public void Http_dtos_belong_to_main_module_without_hosting_dependency_in_shared_contracts()
    {
        Assert.AreEqual(typeof(PaymentsModule).Assembly, typeof(PaymentOrderResponse).Assembly);
        Assert.AreEqual(typeof(PaymentsModule).Assembly, typeof(PaymentRefundResponse).Assembly);
        Assert.AreEqual(typeof(PaymentsModule).Assembly, typeof(SubscriptionCheckoutResponse).Assembly);
        Assert.IsFalse(typeof(PaymentErrorCodes).Assembly.GetReferencedAssemblies().Any(item => item.Name == "Full.NET.Hosting"));
    }

    private static void AssertAmount(string text, string expected)
    {
        using var document = JsonDocument.Parse(text);
        Assert.AreEqual(expected, document.RootElement.GetProperty("amountMinor").GetString());
    }
}
