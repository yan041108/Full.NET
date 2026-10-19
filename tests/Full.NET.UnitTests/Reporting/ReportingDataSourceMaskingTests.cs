using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

/// <summary>单元测试（报表）：Reporting Data Source Masking。</summary>
[TestClass]
public sealed class ReportingDataSourceMaskingTests
{
    [TestMethod]
    public void MaskServerEndpoint_Hides_MiddleOfHost()
    {
        var masked = ReportingDataSourceMasking.MaskServerEndpoint("sql.example.com", 1433);
        Assert.AreEqual("sq***m:1433", masked);
    }

    [TestMethod]
    public void MaskIdentifier_Hides_ShortValues()
    {
        Assert.AreEqual("***", ReportingDataSourceMasking.MaskIdentifier("ab"));
        Assert.AreEqual("re***r", ReportingDataSourceMasking.MaskIdentifier("reader"));
    }
}
