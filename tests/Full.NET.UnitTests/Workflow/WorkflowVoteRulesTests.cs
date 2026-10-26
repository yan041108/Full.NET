using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>投票固定分母、弃权和两个百分比各自裁定，不接受可以被后续投票翻转的部分结果。</summary>
[TestClass]
public sealed class WorkflowVoteRulesTests
{
    [TestMethod]
    [DataRow(3, 2, 1, 0, 0, 100, 67, "Rejected")]
    [DataRow(3, 2, 0, 1, 0, 100, 100, "Approved")]
    [DataRow(3, 1, 1, 1, 0, 100, 51, "Rejected")]
    [DataRow(3, 0, 0, 3, 0, 100, 1, "Suspended")]
    [DataRow(3, 2, 0, 0, 1, 67, 100, "Waiting")]
    [DataRow(3, 2, 0, 0, 1, 66, 100, "Waiting")]
    [DataRow(3, 0, 2, 0, 1, 67, 100, "Waiting")]
    public void Fixed_counts_are_evaluated_only_after_every_member_submits(int eligible, int yes, int no, int abstain,
        int pending, int participation, int approval, string expected)
    {
        var policy = Policy(participation, approval);
        Assert.AreEqual(expected, WorkflowVoteRules.Decide(policy, new(eligible, yes, no, abstain, pending), deadlineReached: false).ToString());
    }

    [TestMethod]
    [DataRow(3, 1, 0, 0, 2, 34, "Rejected")]
    [DataRow(3, 2, 0, 0, 1, 67, "Rejected")]
    [DataRow(3, 2, 0, 0, 1, 66, "Approved")]
    [DataRow(3, 0, 0, 0, 3, 1, "Suspended")]
    public void Trusted_deadline_closes_remaining_candidates_without_counting_them_as_participants(int eligible, int yes,
        int no, int abstain, int pending, int participation, string expected) =>
        Assert.AreEqual(expected, WorkflowVoteRules.Decide(Policy(participation, 100), new(eligible, yes, no, abstain, pending), true).ToString());

    [TestMethod]
    [DataRow(0, 0, 0, 0, 0)]
    [DataRow(21, 21, 0, 0, 0)]
    [DataRow(3, 2, 1, 1, 0)]
    [DataRow(3, -1, 2, 0, 2)]
    public void Inconsistent_or_unbounded_counts_fail_closed(int eligible, int yes, int no, int abstain, int pending) =>
        Assert.AreEqual(WorkflowVoteOutcome.Invalid, WorkflowVoteRules.Decide(Policy(1, 1), new(eligible, yes, no, abstain, pending), true));

    [TestMethod]
    public void Disabled_abstention_cannot_enter_authoritative_tally() =>
        Assert.AreEqual(WorkflowVoteOutcome.Invalid, WorkflowVoteRules.Decide(Policy(1, 1) with { AllowAbstain = false }, new(2, 1, 0, 1, 0), false));

    [TestMethod]
    public void Declared_rule_adds_a_gate_without_overriding_frozen_base_thresholds()
    {
        using var json = JsonDocument.Parse("""{"operatorKey":"and","conditions":[{"metricKey":"yes","comparisonKey":"gte","value":2},{"metricKey":"no","comparisonKey":"eq","value":0}]}""");
        Assert.IsTrue(WorkflowVoteResultRule.TryRead(json.RootElement, out var rule));
        var policy = Policy(100, 50) with { ResultRule = rule };
        Assert.AreEqual(WorkflowVoteOutcome.Rejected, WorkflowVoteRules.Decide(policy, new(3, 2, 1, 0, 0), false));
        Assert.AreEqual(WorkflowVoteOutcome.Approved, WorkflowVoteRules.Decide(policy, new(3, 2, 0, 1, 0), false));
        Assert.AreEqual(WorkflowVoteOutcome.Waiting, WorkflowVoteRules.Decide(policy, new(3, 2, 0, 0, 1), false));
    }

    [TestMethod]
    [DataRow("{\"metricKey\":\"yes\",\"comparisonKey\":\"gte\",\"value\":2,\"script\":\"true\"}")]
    [DataRow("{\"metricKey\":\"arbitraryField\",\"comparisonKey\":\"gte\",\"value\":2}")]
    [DataRow("{\"metricKey\":\"yes\",\"comparisonKey\":\"gte\",\"value\":21}")]
    [DataRow("{\"metricKey\":\"yes\",\"comparisonKey\":\"gte\",\"value\":1.5}")]
    [DataRow("{\"operatorKey\":\"and\",\"conditions\":[]}")]
    [DataRow("{\"metricKey\":\"yes\",\"metricKey\":\"no\",\"comparisonKey\":\"eq\",\"value\":0}")]
    public void Rule_rejects_unknown_script_fields_duplicate_keys_and_unbounded_operands(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowVoteResultRule.TryRead(document.RootElement, out _));
    }

    [TestMethod]
    public void Rule_limits_both_depth_and_total_size()
    {
        var leaf = """{"metricKey":"yes","comparisonKey":"gte","value":1}""";
        var group = (string child) => "{\"operatorKey\":\"and\",\"conditions\":[" + child + "," + leaf + "]}";
        var nested = group(group(group(group(leaf))));
        using var deep = JsonDocument.Parse(nested);
        Assert.IsFalse(WorkflowVoteResultRule.TryRead(deep.RootElement, out _));
        var eight = "{\"operatorKey\":\"or\",\"conditions\":[" + string.Join(',', Enumerable.Repeat(leaf, 8)) + "]}";
        using var wide = JsonDocument.Parse("{\"operatorKey\":\"and\",\"conditions\":[" + string.Join(',', Enumerable.Repeat(eight, 4)) + "]}");
        Assert.IsFalse(WorkflowVoteResultRule.TryRead(wide.RootElement, out _));
    }

    [TestMethod]
    public void Policy_is_closed_and_pins_the_ballot_version()
    {
        var version = Guid.CreateVersion7();
        var body = $$$"""{"votePolicy":{"ballotFormVersionId":"{{{version:D}}}","participationPercentage":67,"approvalPercentage":51,"allowAbstain":true,"resultFieldKey":"passed","closeAfterSeconds":3600}}""";
        using var json = JsonDocument.Parse(body);
        Assert.IsTrue(WorkflowVotePolicy.TryRead(json.RootElement, out var policy));
        Assert.AreEqual(version, policy!.BallotFormVersionId); Assert.AreEqual(3600, policy.CloseAfterSeconds);
        foreach (var invalid in new[] { body.Replace("67", "0"), body.Replace("51", "101"), body.Replace("3600", "59"),
            body.Replace("\"passed\"", "\"nested.field\""), body.Replace("\"allowAbstain\":true", "\"allowAbstain\":true,\"script\":\"true\"") })
        { using var bad = JsonDocument.Parse(invalid); Assert.IsFalse(WorkflowVotePolicy.TryRead(bad.RootElement, out _)); }
    }

    private static WorkflowVotePolicy Policy(int participation, int approval) => new(Guid.CreateVersion7(), participation, approval, true, null, null, null);
}
