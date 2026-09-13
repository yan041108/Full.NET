using System.Data;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>脱离真实数据库验证原生静态物化，尤其两库 SUM 的 decimal 返回类型。</summary>
[TestClass]
public sealed class AiBudgetRowReaderTests
{
    [TestMethod]
    public void Totals_convert_database_decimal_aggregates_without_truncation()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "MonthlyRequests", "MonthlyTokens", "RunRequests", "RunTokens" }) table.Columns.Add(name, typeof(decimal));
        table.Rows.Add(2m, 3000000000m, 1m, 30m);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var totals = AiBudgetRowReaders.ReadTotals(reader);
        Assert.AreEqual(2L, totals.MonthlyRequests); Assert.AreEqual(3000000000L, totals.MonthlyTokens);
        Assert.AreEqual(1L, totals.RunRequests); Assert.AreEqual(30L, totals.RunTokens);
    }

    [TestMethod]
    public void Unknown_operation_preserves_nullable_usage_and_price()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "RunId", "ModelConfigId", "PriceVersionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "RequestHash", "QuotaMonthKey", "Currency", "UsageStatus", "Outcome" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "ReservedTokens", "InputTokens", "OutputTokens", "CachedInputTokens" }) table.Columns.Add(name, typeof(long));
        foreach (var name in new[] { "ReservedCost", "InputPerMillion", "OutputPerMillion", "CachedInputPerMillion" }) table.Columns.Add(name, typeof(decimal));
        table.Columns.Add("LegacyTracked", typeof(byte));
        var row = table.NewRow(); row["Id"] = Guid.NewGuid(); row["ModelConfigId"] = Guid.NewGuid();
        row["RequestHash"] = new string('A', 64); row["QuotaMonthKey"] = "2026-09"; row["UsageStatus"] = "unknown";
        row["Outcome"] = "failed"; row["ReservedTokens"] = 30L; row["LegacyTracked"] = (byte)1;
        table.Rows.Add(row);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadOperation(reader);
        Assert.IsNull(value.ReservedCost); Assert.IsNull(value.InputTokens); Assert.IsNull(value.PriceVersionId);
        Assert.AreEqual(30L, value.ReservedTokens); Assert.IsTrue(value.LegacyTracked); Assert.AreEqual("unknown", value.UsageStatus);
    }
}
