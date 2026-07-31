using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Domain;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialNumberPatternTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        30,
        8,
        9,
        10,
        TimeSpan.Zero);

    [TestMethod]
    public void Valid_pattern_uses_utc_tenant_and_fixed_width_sequence()
    {
        var result = SerialNumberPattern.Parse(
            "SO-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:6}",
            SerialNumberRuleScope.Tenant);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            "SO-20260730-acme-000042",
            result.Value!.Format(Now, "acme", 42));
    }

    [TestMethod]
    public void Unknown_or_unbalanced_tokens_are_rejected()
    {
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{unknown}-{sequence:4}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "SO-{sequence:4",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "SO-}- {sequence:4}",
            SerialNumberRuleScope.Host).IsSuccess);
    }

    [TestMethod]
    public void Pattern_requires_exactly_one_sequence_token()
    {
        Assert.IsFalse(SerialNumberPattern.Parse(
            "SO-{utc:yyyy}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{sequence:4}-{sequence:5}",
            SerialNumberRuleScope.Host).IsSuccess);
    }

    [TestMethod]
    public void Sequence_width_outside_one_to_eighteen_is_rejected()
    {
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{sequence:0}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{sequence:19}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{sequence:x}",
            SerialNumberRuleScope.Host).IsSuccess);
    }

    [TestMethod]
    public void Local_time_tokens_and_tenant_token_in_host_scope_are_rejected()
    {
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{yyyy}-{sequence:4}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            "{tenant}-{sequence:4}",
            SerialNumberRuleScope.Host).IsSuccess);
    }

    [TestMethod]
    public void Pattern_and_worst_case_output_lengths_are_bounded()
    {
        Assert.IsFalse(SerialNumberPattern.Parse(
            $"{new string('A', 129)}{{sequence:1}}",
            SerialNumberRuleScope.Host).IsSuccess);
        Assert.IsFalse(SerialNumberPattern.Parse(
            $"{new string('A', 64)}{{tenant}}{{sequence:18}}",
            SerialNumberRuleScope.Tenant).IsSuccess);
    }
}
