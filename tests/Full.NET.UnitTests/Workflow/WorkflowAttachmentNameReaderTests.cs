using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>安全名称只补当前可见共享引用，不能由隐藏字段、伪造查询行或其他字段类型扩张。</summary>
[TestClass]
public sealed class WorkflowAttachmentNameReaderTests
{
    [TestMethod]
    public async Task No_visible_files_omits_metadata_without_querying()
    {
        var query = Substitute.For<IQueryExecutor>();
        Assert.IsNull(await ReadAsync(query, Schema("attachment"), new Dictionary<string, JsonElement>()));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Only_authorized_schema_file_ids_can_supply_names()
    {
        var visible = Guid.CreateVersion7(); var hidden = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRemoteAttachmentNameRecord>(WorkflowCollaborationFileSql.ReadVisibleNames, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowRemoteAttachmentNameRecord>>([new(visible, "资料.png"), new(hidden, "private.png")]));
        var names = await ReadAsync(query, Schema("attachment"), new Dictionary<string, JsonElement> {
            ["input"] = JsonSerializer.SerializeToElement(new[] { visible }), ["hidden"] = JsonSerializer.SerializeToElement(new[] { hidden }) });
        Assert.HasCount(1, names!); Assert.AreEqual("资料.png", names![visible.ToString("D")]);
        var arguments = (Dictionary<string, object?>)query.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual("host", (string)arguments["TenantScopeKey"]!); Assert.AreEqual(Instance, (Guid)arguments["InstanceId"]!);
        CollectionAssert.AreEqual(new[] { visible }, (Guid[])arguments["FileIds"]!);
    }

    [TestMethod]
    [DataRow("text")][DataRow("subtable")]
    public async Task Other_field_types_cannot_become_file_metadata_lookups(string type)
    {
        var query = Substitute.For<IQueryExecutor>();
        Assert.IsNull(await ReadAsync(query, Schema(type), new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement(new[] { Guid.CreateVersion7() }) }));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Many_visible_files_use_bounded_batches_and_deduplicate_across_fields()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowRemoteAttachmentNameRecord>(WorkflowCollaborationFileSql.ReadVisibleNames, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<WorkflowRemoteAttachmentNameRecord>>(((Guid[])((Dictionary<string, object?>)call[1]!)["FileIds"]!)
                .Select(id => new WorkflowRemoteAttachmentNameRecord(id, "file.png")).ToArray()));
        var ids = Enumerable.Range(0, 260).Select(_ => Guid.CreateVersion7()).ToArray();
        var fields = Enumerable.Range(0, 14).Select(index => new WorkflowFormField("files" + index, "attachment", false, new Dictionary<string, JsonElement>())).ToArray();
        var values = fields.Take(13).Select((field, index) => (field.FieldKey, Value: JsonSerializer.SerializeToElement(ids.Skip(index * 20).Take(20))))
            .ToDictionary(item => item.FieldKey, item => item.Value);
        values[fields[13].FieldKey] = JsonSerializer.SerializeToElement(ids.Take(20));
        var names = await ReadAsync(query, new(1, 1, [new("main", fields)]), values); Assert.HasCount(260, names!);
        var batches = query.ReceivedCalls().Select(call => (Guid[])((Dictionary<string, object?>)call.GetArguments()[1]!)["FileIds"]!).ToArray();
        CollectionAssert.AreEqual(new[] { 256, 4 }, batches.Select(batch => batch.Length).ToArray());
    }

    [TestMethod]
    [DataRow("missing")][DataRow("empty")][DataRow("too_long")]
    public async Task Missing_or_invalid_metadata_keeps_the_opaque_reference(string state)
    {
        var file = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        IReadOnlyList<WorkflowRemoteAttachmentNameRecord> rows = state == "missing" ? [] : [new(file, state == "empty" ? "" : new string('a', 261))];
        query.QueryAsync<WorkflowRemoteAttachmentNameRecord>(WorkflowCollaborationFileSql.ReadVisibleNames, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));
        Assert.IsNull(await ReadAsync(query, Schema("attachment"), new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement(new[] { file }) }));
    }

    [TestMethod]
    public void Generated_contract_preserves_unicode_names_and_omits_absent_metadata()
    {
        var value = new WorkflowTodoDetailResponse(Guid.CreateVersion7(), Instance, Guid.CreateVersion7(), Guid.CreateVersion7(), "active", 1,
            Guid.CreateVersion7(), JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), new Dictionary<string, string>(), 1)
            { ApprovalModeKey = "single", RequiredApprovalCount = 1, ApprovedCount = 0, RejectedCount = 0, PendingCount = 1 };
        Assert.IsFalse(JsonSerializer.SerializeToElement(value, WorkflowJsonSerializerContext.Default.WorkflowTodoDetailResponse).TryGetProperty("attachmentNames", out _));
        value = value with { AttachmentNames = new Dictionary<string, string> { [Guid.CreateVersion7().ToString("D")] = "安全资料.png" } };
        var json = JsonSerializer.SerializeToElement(value, WorkflowJsonSerializerContext.Default.WorkflowTodoDetailResponse);
        Assert.AreEqual("安全资料.png", json.GetProperty("attachmentNames").EnumerateObject().Single().Value.GetString());
    }

    private static readonly Guid Instance = Guid.CreateVersion7();
    private static WorkflowFormSchema Schema(string type) => new(1, 1, [new("main", [new("input", type, false, new Dictionary<string, JsonElement>())])]);
    private static Task<IReadOnlyDictionary<string, string>?> ReadAsync(IQueryExecutor query, WorkflowFormSchema schema, IReadOnlyDictionary<string, JsonElement> values) =>
        WorkflowAttachmentNameReader.ReadAsync(Instance, new(null, "host", "host"), schema, values, query, default);
}
