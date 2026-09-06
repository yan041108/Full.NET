using Full.NET.Abstractions.Auditing;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class DomainAuditChangeDiffParserTests
{
    [TestMethod]
    public void ParseAvailability_returns_no_diff_when_json_missing()
    {
        var availability = DomainAuditChangeDiffParser.ParseAvailability(null);

        Assert.AreEqual(DomainAuditChangeDiffAvailability.NoDiffRecorded, availability);
        Assert.AreEqual(0, DomainAuditChangeDiffParser.ParseFields(null).Count);
    }

    [TestMethod]
    public void ParseFields_maps_before_and_after_pairs()
    {
        const string json = """{"pressure":{"before":"low","after":"high"}}""";

        var availability = DomainAuditChangeDiffParser.ParseAvailability(json);
        var fields = DomainAuditChangeDiffParser.ParseFields(json);

        Assert.AreEqual(DomainAuditChangeDiffAvailability.Available, availability);
        Assert.HasCount(1, fields);
        Assert.AreEqual("pressure", fields[0].FieldKey);
        Assert.AreEqual("low", fields[0].BeforeValue);
        Assert.AreEqual("high", fields[0].AfterValue);
    }

    [TestMethod]
    public void ParseFields_redacts_sensitive_values()
    {
        const string json = """{"password":"Bearer secret-token"}""";

        var fields = DomainAuditChangeDiffParser.ParseFields(json);

        Assert.HasCount(1, fields);
        Assert.AreEqual("field.redacted", fields[0].FieldKey);
        Assert.AreEqual("[redacted]", fields[0].AfterValue);
    }

    [TestMethod]
    public void ParseAvailability_returns_unparseable_for_invalid_json()
    {
        var availability = DomainAuditChangeDiffParser.ParseAvailability("{not-json");

        Assert.AreEqual(DomainAuditChangeDiffAvailability.Unparseable, availability);
    }
}
