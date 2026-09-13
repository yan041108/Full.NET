using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAgentToolAuditPolicyTests
{
    [TestMethod]
    public void Summarize_redacts_api_key_like_values()
    {
        var summary = AiAgentToolAuditPolicy.Summarize("token=sk-abcdefghijklmnopqrstuvwxyz");

        Assert.Contains("[redacted]", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", summary, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Summarize_omits_untrusted_long_text()
    {
        var summary = AiAgentToolAuditPolicy.Summarize(new string('A', 600));

        Assert.IsLessThanOrEqualTo(AiAgentToolAuditPolicy.MaxSummaryLength, summary.Length);
        Assert.AreEqual("[redacted]", summary);
    }
}
