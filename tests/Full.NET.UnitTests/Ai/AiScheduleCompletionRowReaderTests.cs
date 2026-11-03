using System.Data;
using Full.NET.Modules.Ai.Persistence;
namespace Full.NET.UnitTests.Ai;

/// <summary>两个提供程序的整数表示及缺失回执，不能把Host空租户或accepted转换成完成。</summary>
[TestClass]
public sealed class AiScheduleCompletionRowReaderTests
{
    [TestMethod][DataRow(false,false)][DataRow(false,true)][DataRow(true,false)][DataRow(true,true)]
    public void Original_intent_projection_preserves_scope_ids_and_nullable_receipt(bool mysql,bool absent)
    {
        using var table=new DataTable();
        foreach(var name in new[]{"Id","TenantId","OwnerUserId","RunId","OperationId"})table.Columns.Add(name,typeof(Guid));
        foreach(var name in new[]{"RequestJson","RequestDigest","Outcome","ReceiptJson"})table.Columns.Add(name,typeof(string));
        table.Columns.Add("Version",mysql?typeof(long):typeof(int));var id=Guid.CreateVersion7();var tenant=Guid.CreateVersion7();var actor=Guid.CreateVersion7();var run=Guid.CreateVersion7();var operation=Guid.CreateVersion7();
        table.Rows.Add(id,absent?DBNull.Value:tenant,actor,run,operation,"{\"bound\":true}",new string('A',64),absent?"accepted":"completed",absent?DBNull.Value:"{\"receipt\":true}",17);
        using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());var row=AiBudgetRowReaders.ReadScheduleCompletionIntent(reader);
        Assert.AreEqual(id,row.Id);Assert.AreEqual(absent?null:(Guid?)tenant,row.TenantId);Assert.AreEqual(actor,row.OwnerUserId);Assert.AreEqual(run,row.RunId);Assert.AreEqual(operation,row.OperationId);
        Assert.AreEqual("{\"bound\":true}",row.RequestJson);Assert.AreEqual(new string('A',64),row.RequestDigest);Assert.AreEqual(absent?"accepted":"completed",row.Outcome);
        Assert.AreEqual(absent?null:"{\"receipt\":true}",row.ReceiptJson);Assert.AreEqual(17,row.Version);
    }
}
