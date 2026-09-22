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

    [TestMethod]
    public void Mcp_remote_tool_maps_executable_catalog_columns()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "ConnectionId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[]
                 {
                     "ConnectionKey", "EndpointUrl", "LocalToolName", "RemoteToolName", "InputSchemaJson",
                     "InputSchemaHash", "SideEffectKey", "PermissionCode", "ApprovalStatusKey", "ServiceTokenProtected"
                 })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("ToolVersion", typeof(int));
        var connectionId = Guid.NewGuid();
        var row = table.NewRow();
        row["ConnectionId"] = connectionId;
        row["ConnectionKey"] = "demo";
        row["EndpointUrl"] = "https://mcp.example";
        row["LocalToolName"] = "local.tool";
        row["RemoteToolName"] = "remote.tool";
        row["ToolVersion"] = 2;
        row["InputSchemaJson"] = "{}";
        row["InputSchemaHash"] = "abc";
        row["SideEffectKey"] = "read";
        row["PermissionCode"] = "ai.agent-tools.view";
        row["ApprovalStatusKey"] = "approved";
        row["ServiceTokenProtected"] = "protected";
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadMcpRemoteTool(reader);
        Assert.AreEqual(connectionId, value.ConnectionId);
        Assert.AreEqual("local.tool", value.LocalToolName);
        Assert.AreEqual(2, value.ToolVersion);
        Assert.AreEqual("approved", value.ApprovalStatusKey);
    }

    [TestMethod]
    public void Agent_approval_maps_nullable_columns()
    {
        using var table = new DataTable();
        foreach (var name in new[] { "Id", "RunId", "OperationId", "SessionId", "RequestedBy" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "ScopeKey", "ToolName", "ArgumentsHash", "ArgumentsProtected", "PresentationJson", "DecisionKey" })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("TenantId", typeof(Guid));
        table.Columns.Add("ApproverId", typeof(Guid));
        table.Columns.Add("ToolVersion", typeof(int));
        table.Columns.Add("PolicyVersion", typeof(int));
        table.Columns.Add("Version", typeof(long));
        table.Columns.Add("ExpiresAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("ConsumedAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("CreatedAtUtc", typeof(DateTimeOffset));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTimeOffset));
        var id = Guid.NewGuid();
        var row = table.NewRow();
        row["Id"] = id;
        row["ScopeKey"] = "host";
        row["TenantId"] = DBNull.Value;
        row["RunId"] = Guid.NewGuid();
        row["OperationId"] = Guid.NewGuid();
        row["SessionId"] = Guid.NewGuid();
        row["ToolName"] = "demo.tool";
        row["ToolVersion"] = 1;
        row["ArgumentsHash"] = "hash";
        row["ArgumentsProtected"] = "protected";
        row["PolicyVersion"] = 2;
        row["PresentationJson"] = "{}";
        row["RequestedBy"] = Guid.NewGuid();
        row["ApproverId"] = DBNull.Value;
        row["DecisionKey"] = "pending";
        row["ExpiresAtUtc"] = DateTimeOffset.UtcNow;
        row["ConsumedAtUtc"] = DBNull.Value;
        row["Version"] = 1L;
        row["CreatedAtUtc"] = DateTimeOffset.UtcNow;
        row["UpdatedAtUtc"] = DateTimeOffset.UtcNow;
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var value = AiBudgetRowReaders.ReadAgentApproval(reader);
        Assert.AreEqual(id, value.Id);
        Assert.IsNull(value.TenantId);
        Assert.IsNull(value.ApproverId);
        Assert.IsNull(value.ConsumedAtUtc);
        Assert.AreEqual("pending", value.DecisionKey);
    }
}
