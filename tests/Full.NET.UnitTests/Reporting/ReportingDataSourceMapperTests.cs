using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDataSources;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingDataSourceMapperTests
{
    [TestMethod]
    public void Historical_failed_test_message_is_not_returned()
    {
        var row = new ReportingDataSourceRecord
        {
            LastTestStatusKey = ReportingDataSourceTestStatusKeys.Failed,
            LastTestMessage = "Password=old-secret;Server=internal-db",
        };

        var detail = ReportingDataSourceMapper.MapDetail(row);
        var listItem = ReportingDataSourceMapper.MapListItem(row);

        Assert.AreEqual("Reporting data source test failed.", detail.LastTestMessage);
        Assert.AreEqual("Reporting data source test failed.", listItem.LastTestMessage);
    }
}
