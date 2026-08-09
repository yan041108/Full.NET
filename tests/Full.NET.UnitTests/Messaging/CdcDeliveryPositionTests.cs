using Full.NET.Messaging.Abstractions;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class CdcDeliveryPositionTests
{
    [TestMethod]
    public void MySql_connector_position_covers_same_file_and_higher_offset()
    {
        var fence = CdcDeliveryPosition.ForMySql(
            Guid.CreateVersion7(),
            "mysql-bin.000003",
            154);
        var connector = CdcDeliveryPosition.ForMySql(null, "mysql-bin.000003", 200);

        Assert.IsTrue(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, connector));
    }

    [TestMethod]
    public void MySql_connector_position_rejects_lower_offset_on_same_file()
    {
        var fence = CdcDeliveryPosition.ForMySql(
            Guid.CreateVersion7(),
            "mysql-bin.000003",
            200);
        var connector = CdcDeliveryPosition.ForMySql(null, "mysql-bin.000003", 154);

        Assert.IsFalse(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, connector));
    }

    [TestMethod]
    public void MySql_connector_position_accepts_newer_binlog_file()
    {
        var fence = CdcDeliveryPosition.ForMySql(
            Guid.CreateVersion7(),
            "mysql-bin.000003",
            9_999);
        var connector = CdcDeliveryPosition.ForMySql(null, "mysql-bin.000004", 1);

        Assert.IsTrue(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, connector));
    }

    [TestMethod]
    public void SqlServer_connector_position_covers_higher_lsn()
    {
        var fence = CdcDeliveryPosition.ForSqlServer(
            Guid.CreateVersion7(),
            "00000027:00000123:0001");
        var connector = CdcDeliveryPosition.ForSqlServer(null, "00000027:00000123:00aa");

        Assert.IsTrue(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, connector));
    }

    [TestMethod]
    public void SqlServer_connector_position_rejects_lower_lsn()
    {
        var fence = CdcDeliveryPosition.ForSqlServer(
            Guid.CreateVersion7(),
            "00000027:00000123:00aa");
        var connector = CdcDeliveryPosition.ForSqlServer(null, "00000027:00000123:0001");

        Assert.IsFalse(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, connector));
    }

    [TestMethod]
    public void SqlServer_lsn_bytes_roundtrip_matches_colon_format()
    {
        var bytes = new byte[]
        {
            0x27, 0x00, 0x00, 0x00,
            0x23, 0x01, 0x00, 0x00,
            0x01, 0x00,
        };

        var position = CdcDeliveryPosition.ForSqlServerBytes(Guid.CreateVersion7(), bytes);

        Assert.AreEqual("00000027:00000123:0001", position.Lsn!.CommitLsn);
    }

    [TestMethod]
    public void Position_json_roundtrip_preserves_mysql_coordinates()
    {
        var original = CdcDeliveryPosition.ForMySql(
            Guid.CreateVersion7(),
            "mysql-bin.000010",
            42);

        Assert.IsTrue(CdcDeliveryPosition.TryParse(original.ToJson(), out var parsed));
        Assert.IsNotNull(parsed);
        Assert.AreEqual(CdcDeliveryPosition.MySqlProvider, parsed!.Provider);
        Assert.AreEqual("mysql-bin.000010", parsed.Binlog!.File);
        Assert.AreEqual(42, parsed.Binlog.Position);
    }
}
