using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialNumberRuleListFilterTests
{
    [TestMethod]
    public void NormalizeScopeFilter_accepts_defined_scope_values_only()
    {
        Assert.AreEqual(0, HostSerialRuleService.NormalizeScopeFilter(0));
        Assert.AreEqual(1, HostSerialRuleService.NormalizeScopeFilter(1));
        Assert.IsNull(HostSerialRuleService.NormalizeScopeFilter(2));
        Assert.IsNull(HostSerialRuleService.NormalizeScopeFilter(null));
    }

    [TestMethod]
    public void NormalizeResetIntervalFilter_accepts_defined_interval_values_only()
    {
        Assert.AreEqual(0, HostSerialRuleService.NormalizeResetIntervalFilter(0));
        Assert.AreEqual(3, HostSerialRuleService.NormalizeResetIntervalFilter(3));
        Assert.IsNull(HostSerialRuleService.NormalizeResetIntervalFilter(4));
        Assert.IsNull(HostSerialRuleService.NormalizeResetIntervalFilter(null));
    }
}
