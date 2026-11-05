using System.Text.Json;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Serialization;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>用量读模型使用精确线格式，不借助 JavaScript 安全整数范围的假设。</summary>
[TestClass]
public sealed class TenantQuotaUsageContractTests
{
    [TestMethod]
    public void Usage_preserves_int64_as_decimal_strings_with_source_generated_json()
    {
        var value = new TenantQuotaUsageSummaryResponse(Guid.CreateVersion7(), true, DateTimeOffset.UtcNow,
            [new(Guid.CreateVersion7(), TenantQuotaMetricCodes.FilesStorageBytes, TenantQuotaDefaults.PeriodKey, long.MaxValue, long.MaxValue - 100, 25)]);
        var json = JsonSerializer.Serialize(value, TenancyJsonSerializerContext.Default.TenantQuotaUsageSummaryResponse);
        using var document = JsonDocument.Parse(json);
        var metric = document.RootElement.GetProperty("metrics")[0];
        Assert.AreEqual("9223372036854775807", metric.GetProperty("limitValue").GetString());
        Assert.AreEqual("9223372036854775707", metric.GetProperty("usedValue").GetString());
        Assert.AreEqual("25", metric.GetProperty("reservedValue").GetString());
        var copy = JsonSerializer.Deserialize(json, TenancyJsonSerializerContext.Default.TenantQuotaUsageSummaryResponse)!;
        Assert.AreEqual(long.MaxValue, copy.Metrics[0].LimitValue);
        Assert.AreEqual(value.TenantId, copy.TenantId);
    }

    [TestMethod]
    public void Usage_rejects_numeric_tokens_instead_of_coercing_lost_precision()
    {
        var value = new TenantQuotaUsageSummaryResponse(Guid.CreateVersion7(), true, DateTimeOffset.UtcNow,
            [new(Guid.CreateVersion7(), TenantQuotaMetricCodes.IdentitySeats, TenantQuotaDefaults.PeriodKey, 100, 2, 1)]);
        var json = JsonSerializer.Serialize(value, TenancyJsonSerializerContext.Default.TenantQuotaUsageSummaryResponse).Replace("\"limitValue\":\"100\"", "\"limitValue\":100", StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, TenancyJsonSerializerContext.Default.TenantQuotaUsageSummaryResponse));
    }
}
