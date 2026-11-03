using System.Data;
using Dapper;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>双库聚合可能返回Int32/Int64/Decimal；JIT属性映射与原生静态读取必须保留同一空值和金额。</summary>
[TestClass]
public sealed class AiKnowledgeFinanceRowReaderTests
{
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Finance_jit_and_native_read_actual_provider_aggregate_types_without_constructor_assumptions(bool mysql)
    {
        var data=new Dictionary<string,object?>{["KnowledgeBaseId"]=null,["Month"]="2026-10",["PriceVersionId"]=null,["Currency"]="USD",
            ["Requests"]=mysql?(object)2L:2,["Known"]=mysql?(object)1m:1,["Unknown"]=mysql?(object)1m:1,["Reserved"]=mysql?(object)0m:0,["MissingCost"]=mysql?(object)1m:1,
            ["RecordedCost"]=0.125m,["ConservativeReservation"]=null};
        using var table=Table(data);using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());
        var jit=reader.GetRowParser<AiKnowledgeFinanceRow>()(reader);var native=AiBudgetRowReaders.ReadKnowledgeFinance(reader);
        Assert.AreEqual(jit,native);Assert.AreEqual(2L,native.Requests);Assert.AreEqual(0.125m,native.RecordedCost);Assert.IsNull(native.ConservativeReservation);Assert.IsNull(native.KnowledgeBaseId);
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Observation_empty_measurement_is_not_zero_in_both_materializers(bool mysql)
    {
        var data=new Dictionary<string,object?>{["KnowledgeBaseId"]=Guid.CreateVersion7(),["RecordedAnswers"]=mysql?(object)2L:2,["Refused"]=mysql?(object)1L:1,["Measured"]=mysql?(object)0L:0,
            ["MeanPipelineMs"]=null,["FirstRecordedAtUtc"]=null,["CurrentNegativeFeedback"]=mysql?(object)1L:1};
        using var table=Table(data);using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());
        var jit=reader.GetRowParser<AiKnowledgeQualityObservationRow>()(reader);var native=AiBudgetRowReaders.ReadKnowledgeQualityObservation(reader);
        Assert.AreEqual(jit,native);Assert.IsNull(native.MeanPipelineMs);Assert.AreEqual(0L,native.Measured);Assert.AreEqual(1L,native.Refused);
    }
    private static DataTable Table(Dictionary<string,object?> values)
    {var table=new DataTable();foreach(var pair in values)table.Columns.Add(pair.Key,pair.Value?.GetType()??(pair.Key.Contains("Cost",StringComparison.Ordinal)||pair.Key.Contains("Reservation",StringComparison.Ordinal)||pair.Key=="MeanPipelineMs"?typeof(decimal):pair.Key.EndsWith("AtUtc",StringComparison.Ordinal)?typeof(DateTime):typeof(Guid)));table.Rows.Add(values.Values.Select(value=>value??DBNull.Value).ToArray());return table;}
}
