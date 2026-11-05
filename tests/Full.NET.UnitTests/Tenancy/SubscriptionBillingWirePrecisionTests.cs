using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Serialization;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>报价及账单 HTTP 金额/限额保留完整 Int64 精度，不改变进程内支付数值。</summary>
[TestClass]
public sealed class SubscriptionBillingWirePrecisionTests
{
    private static UpsertTenantPackageOfferRequest Request() => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        9_007_199_254_740_993L, "CNY", long.MaxValue, long.MaxValue - 1, 30, [], true, 0);

    [TestMethod]
    public void Offer_request_and_response_round_trip_exact_string_int64_values()
    {
        var request = Request();
        var json = JsonSerializer.Serialize(request, TenancyJsonSerializerContext.Default.UpsertTenantPackageOfferRequest);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("9007199254740993", document.RootElement.GetProperty("amountMinor").GetString());
        Assert.AreEqual("9223372036854775807", document.RootElement.GetProperty("seatLimit").GetString());
        Assert.AreEqual("9223372036854775806", document.RootElement.GetProperty("storageLimit").GetString());
        var restored = JsonSerializer.Deserialize(json, TenancyJsonSerializerContext.Default.UpsertTenantPackageOfferRequest)!;
        Assert.AreEqual(request.AmountMinor, restored.AmountMinor); Assert.AreEqual(request.SeatLimit, restored.SeatLimit);
        Assert.AreEqual(request.StorageLimit, restored.StorageLimit);
        var response = new TenantPackageOfferResponse(Guid.CreateVersion7(), request.PaymentTenantId, request.MerchantConfigId,
            request.AmountMinor, request.Currency, request.SeatLimit, request.StorageLimit, request.DurationDays, [], true, 1);
        var wire = JsonSerializer.Serialize(response, TenancyJsonSerializerContext.Default.TenantPackageOfferResponse);
        using var receipt = JsonDocument.Parse(wire);
        Assert.AreEqual("9007199254740993", receipt.RootElement.GetProperty("amountMinor").GetString());
        Assert.AreEqual("9223372036854775807", receipt.RootElement.GetProperty("seatLimit").GetString());
    }

    [TestMethod] [DataRow("9007199254740993")] [DataRow("\"01\"")] [DataRow("\"1e3\"")]
    [DataRow("\"9223372036854775808\"")] [DataRow("null")]
    public void Numeric_or_noncanonical_offer_amount_is_rejected_without_rounding(string invalid)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Request(), TenancyJsonSerializerContext.Default.UpsertTenantPackageOfferRequest))!;
        json["amountMinor"] = JsonNode.Parse(invalid);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json.ToJsonString(), TenancyJsonSerializerContext.Default.UpsertTenantPackageOfferRequest));
    }

    [TestMethod]
    public void Paid_http_receipt_preserves_exact_amount_identity_and_renewal_source()
    {
        var now = DateTimeOffset.UtcNow;
        var terms = new TenantSubscriptionPaymentTerms(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 3, long.MaxValue, "CNY", now, now.AddDays(30), "原商品")
        { RenewalOfSubscriptionId = Guid.CreateVersion7(), RenewalSourceVersion = 7 };
        var response = CreatePaidTenantSubscriptionResponse.From(terms);
        Assert.AreEqual(long.MaxValue, terms.AmountMinor); Assert.AreEqual(terms.SubscriptionId, response.SubscriptionId);
        Assert.AreEqual(terms.RenewalOfSubscriptionId, response.RenewalOfSubscriptionId); Assert.AreEqual(7, response.RenewalSourceVersion);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, TenancyJsonSerializerContext.Default.CreatePaidTenantSubscriptionResponse));
        Assert.AreEqual("9223372036854775807", document.RootElement.GetProperty("amountMinor").GetString());
        Assert.AreEqual(terms.RenewalOfSubscriptionId, document.RootElement.GetProperty("renewalOfSubscriptionId").GetGuid());
    }
}
