using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingContainsTimeRangePolicyTests
{
    private static readonly DateTimeOffset WindowStart =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Options_default_to_a_1_day_contains_window()
    {
        var options = new AuditingQueryOptions();

        Assert.AreEqual(1, options.MaximumContainsWindowDays);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(32)]
    public void Options_validator_rejects_values_outside_supported_range(
        int maximumContainsWindowDays)
    {
        var options = new AuditingQueryOptions
        {
            MaximumContainsWindowDays = maximumContainsWindowDays,
        };

        var result = new AuditingQueryOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Failed);
        CollectionAssert.Contains(
            result.Failures.ToArray(),
            "Auditing:Query:MaximumContainsWindowDays must be between 1 and 31.");
    }

    [TestMethod]
    public void Policy_allows_an_unbounded_query_without_contains()
    {
        var policy = CreatePolicy();

        var error = policy.Validate(
            fromUtc: null,
            toUtc: null,
            hasContains: false);

        Assert.IsNull(error);
    }

    [TestMethod]
    public void Policy_requires_both_boundaries_when_contains_is_active()
    {
        var policy = CreatePolicy();

        var missingFrom = policy.Validate(
            fromUtc: null,
            toUtc: WindowStart.AddDays(1),
            hasContains: true);
        var missingTo = policy.Validate(
            fromUtc: WindowStart,
            toUtc: null,
            hasContains: true);

        AssertError(
            missingFrom,
            AuditingErrorCodes.ContainsTimeRangeRequired);
        AssertError(
            missingTo,
            AuditingErrorCodes.ContainsTimeRangeRequired);
    }

    [TestMethod]
    public void Policy_rejects_a_reversed_time_range()
    {
        var policy = CreatePolicy();

        var error = policy.Validate(
            fromUtc: WindowStart.AddMinutes(1),
            toUtc: WindowStart,
            hasContains: true);

        AssertError(error, AuditingErrorCodes.TimeRangeInvalid);
    }

    [TestMethod]
    public void Policy_rejects_a_contains_window_above_the_configured_limit()
    {
        var policy = CreatePolicy(maximumContainsWindowDays: 31);

        var error = policy.Validate(
            fromUtc: WindowStart,
            toUtc: WindowStart.AddDays(31).AddTicks(1),
            hasContains: true);

        AssertError(
            error,
            AuditingErrorCodes.ContainsTimeRangeExceeded);
    }

    [TestMethod]
    public void Policy_allows_a_contains_window_at_the_configured_limit()
    {
        var policy = CreatePolicy(maximumContainsWindowDays: 31);

        var error = policy.Validate(
            fromUtc: WindowStart,
            toUtc: WindowStart.AddDays(31),
            hasContains: true);

        Assert.IsNull(error);
    }

    private static AuditingContainsTimeRangePolicy CreatePolicy(
        int maximumContainsWindowDays = 31) =>
        new(Options.Create(new AuditingQueryOptions
        {
            MaximumContainsWindowDays = maximumContainsWindowDays,
        }));

    private static void AssertError(Error? error, string expectedCode)
    {
        Assert.IsNotNull(error);
        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual(ErrorType.Validation, error.Type);
    }
}
