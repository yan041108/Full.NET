using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingDefinitionKeyValidatorTests
{
    [TestMethod]
    public void IsValid_accepts_stable_machine_key()
    {
        Assert.IsTrue(ReportingDefinitionKeyValidator.IsValid("sales.monthly_summary"));
    }

    [TestMethod]
    public void IsValid_rejects_uppercase_or_spaces()
    {
        Assert.IsFalse(ReportingDefinitionKeyValidator.IsValid("Sales.Monthly"));
        Assert.IsFalse(ReportingDefinitionKeyValidator.IsValid("sales monthly"));
        Assert.IsFalse(ReportingDefinitionKeyValidator.IsValid(""));
    }
}
