using System.Text.Json;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Serialization;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenancyJsonSerializerContextTests
{
    [TestMethod]
    public void TenantSummary_RoundTripsWithGeneratedWebContract()
    {
        var expected = new TenantSummary(
            Guid.CreateVersion7(),
            "acme",
            "Acme Ltd",
            "acme.localhost",
            true,
            7);

        var json = JsonSerializer.Serialize(
            expected,
            TenancyJsonSerializerContext.Default.TenantSummary);
        var actual = JsonSerializer.Deserialize(
            json,
            TenancyJsonSerializerContext.Default.TenantSummary);

        StringAssert.Contains(json, "\"identifier\":\"acme\"");
        Assert.DoesNotContain("\"Identifier\"", json, StringComparison.Ordinal);
        Assert.AreEqual(expected, actual);
    }
}
