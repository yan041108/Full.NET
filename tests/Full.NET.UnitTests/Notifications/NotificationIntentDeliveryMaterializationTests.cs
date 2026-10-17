using System.Data;
using Dapper;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Notifications.Features.ReadIntentDelivery;

namespace Full.NET.UnitTests.Notifications;

/// <summary>以 Dapper 实际行解析器验证双库 COUNT 的 Int32/Int64 返回形状，不替代真实 SQL。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NotificationIntentDeliveryMaterializationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Jit_row_parser_reads_provider_count_shapes(bool mysqlShape)
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetTypeHandler());
        var id = Guid.NewGuid(); var now = DateTime.UtcNow;
        using var table = new DataTable(); table.Columns.Add("Id", typeof(Guid));
        foreach (var name in new[] { "TenantScopeKey", "ProducerKey", "IdempotencyKey", "StatusKey" }) table.Columns.Add(name, typeof(string));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        foreach (var name in new[] { "TotalDeliveryCount", "PendingDeliveryCount", "SentDeliveryCount", "FailedDeliveryCount", "DeadLetteredDeliveryCount", "UnknownDeliveryCount", "OtherDeliveryCount" })
            table.Columns.Add(name, mysqlShape ? typeof(long) : typeof(int));
        table.Columns.Add("NextAttemptAtUtc", typeof(DateTime));
        foreach (var name in new[] { "PersistedDeliveryCount", "DeliveredDeliveryCount", "ReadDeliveryCount", "SuppressedDeliveryCount" })
            table.Columns.Add(name, mysqlShape ? typeof(long) : typeof(int));
        table.Rows.Add(id, "host", "workflow", "workflow-result", "accepted", now, 11, 3, 1, 1, 1, 1, 0, now.AddMinutes(1), 1, 1, 1, 1);
        using var reader = table.CreateDataReader(); var parse = reader.GetRowParser<NotificationIntentDeliveryRecord>();
        Assert.IsTrue(reader.Read()); var row = parse(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(11, row.TotalDeliveryCount); Assert.AreEqual(3, row.PendingDeliveryCount);
        Assert.AreEqual(1, row.SentDeliveryCount); Assert.AreEqual(1, row.PersistedDeliveryCount);
        Assert.AreEqual(1, row.DeliveredDeliveryCount); Assert.AreEqual(1, row.ReadDeliveryCount); Assert.AreEqual(1, row.SuppressedDeliveryCount);
    }
}
