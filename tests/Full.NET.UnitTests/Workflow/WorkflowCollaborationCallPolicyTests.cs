using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>固定授权与最小源投影不允许任意目标上下文、额外政策和裸附件越过协作边界。</summary>
[TestClass]
public sealed class WorkflowCollaborationCallPolicyTests
{
    private static readonly Guid Grant = Guid.CreateVersion7();
    private static readonly string PolicyHash = new('a', 64);
    private static JsonElement Config(int revision = 2) => JsonSerializer.SerializeToElement(new
    {
        nextNodeKeys = new[] { "end" }, collaborationPolicy = new { schemaVersion = 1, grantId = Grant, grantRevision = revision, policyHash = PolicyHash }
    });
    private static WorkflowFormSchema Schema(string type = "text") => new(1, 1,
        [new("main", [new("title", type, false, new Dictionary<string, JsonElement>()), new("secret", "text", false, new Dictionary<string, JsonElement>())])]);
    private static WorkflowCollaborationOfferPolicy Offer() => new(1, Guid.CreateVersion7(), Guid.CreateVersion7(), "target.flow", ["title"], [], 64, DateTimeOffset.UtcNow.AddHours(1));
    private static WorkflowCollaborationAcceptancePolicy Acceptance() => new(1, Guid.CreateVersion7(), Guid.CreateVersion7(), 32, [],
        new Dictionary<string, string> { ["title"] = "subject" }, new Dictionary<string, string>(), new('b', 64));

    [TestMethod]
    public void Policy_only_accepts_fixed_accepted_revision_and_no_extra_target_authority()
    {
        Assert.IsTrue(WorkflowCollaborationCallPolicy.TryRead(Config(), out var policy));
        Assert.AreEqual(Grant, policy!.GrantId); Assert.AreEqual(2L, policy.GrantRevision);
        Assert.IsFalse(WorkflowCollaborationCallPolicy.TryRead(Config(3), out _));
        using var extra = JsonDocument.Parse(Config().GetRawText().Replace("\"schemaVersion\":1", "\"tenantId\":\"foreign\",\"schemaVersion\":1", StringComparison.Ordinal));
        Assert.IsFalse(WorkflowCollaborationCallPolicy.TryRead(extra.RootElement, out _));
    }

    [TestMethod]
    public void Projection_contains_only_target_accepted_fields_and_copies_values()
    {
        var values = new Dictionary<string, JsonElement>();
        using (var document = JsonDocument.Parse("{\"title\":\"shared\",\"secret\":\"private\"}"))
        {
            foreach (var property in document.RootElement.EnumerateObject()) values.Add(property.Name, property.Value);
            Assert.IsTrue(WorkflowCollaborationCallPolicy.TryProject(Schema(), Offer(), Acceptance(), values, out var projected));
            Assert.AreEqual("{\"subject\":\"shared\"}", projected);
        }
        Assert.IsTrue(WorkflowCollaborationCallPolicy.TryProject(Schema(), Offer(), Acceptance(), new Dictionary<string, JsonElement>(), out var sparse));
        Assert.AreEqual("{}", sparse);
    }

    [TestMethod]
    [DataRow("attachment", "[\"019a0000-0000-7000-8000-000000000001\"]")]
    [DataRow("subtable", "[{\"value\":\"secret\"}]")]
    [DataRow("text", "{\"fileId\":\"secret\"}")]
    [DataRow("text", "12")]
    public void Files_rows_and_wrong_scalar_types_fail_closed(string type, string json)
    {
        using var value = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowCollaborationCallPolicy.TryProject(Schema(type), Offer(), Acceptance(), new Dictionary<string, JsonElement> { ["title"] = value.RootElement }, out _));
    }

    [TestMethod]
    public void File_ids_are_only_local_preparation_inputs_until_the_worker_freezes_capabilities()
    {
        var field = new WorkflowFormField("title", "attachment", false, new Dictionary<string, JsonElement> {
            ["maxCount"] = JsonSerializer.SerializeToElement(2), ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024),
            ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "png" }) });
        var schema = new WorkflowFormSchema(1, 1, [new("main", [field])]);
        var values = new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement(new[] { Guid.CreateVersion7() }) };
        Assert.IsFalse(WorkflowCollaborationCallPolicy.TryProject(schema, Offer(), Acceptance(), values, out _));
        Assert.IsTrue(WorkflowCollaborationCallPolicy.TryProject(schema, Offer(), Acceptance(), values, out _, preparingFiles: true));
    }

    [TestMethod]
    [DataRow("[{\"label\":\"valid\"}]", true)]
    [DataRow("[{\"label\":\"too long text\"}]", false)]
    [DataRow("[{\"label\":\"valid\",\"secret\":\"extra\"}]", false)]
    public void Closed_subtables_validate_the_actual_column_schema(string json, bool expected)
    {
        var field = new WorkflowFormField("title", "subtable", false, new Dictionary<string, JsonElement> {
            ["maxRows"] = JsonSerializer.SerializeToElement(2), ["columns"] = JsonSerializer.SerializeToElement(new[] {
                new { columnKey = "label", fieldTypeKey = "text", required = true, constraints = new { maxLength = 5 } } }) });
        var schema = new WorkflowFormSchema(1, 1, [new("main", [field])]);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(expected, WorkflowCollaborationCallPolicy.TryProject(schema, Offer(), Acceptance(),
            new Dictionary<string, JsonElement> { ["title"] = document.RootElement }, out _));
    }

    [TestMethod]
    public void Internal_wait_can_be_built_but_public_design_and_publish_remain_closed()
    {
        var draft = new WorkflowDefinitionDraft(1,
            [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "collaborate" } })),
             new("collaborate", WorkflowCollaborationCallPolicy.NodeType, 1, Config()), new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))], "automation");
        Assert.IsFalse(WorkflowNodeTypeCatalog.TryGet(WorkflowCollaborationCallPolicy.NodeType, out _));
        Assert.AreEqual(WorkflowErrorCodes.DefinitionNodeTypeUnknown, WorkflowDefinitionCompiler.Compile(draft).ErrorCode);
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft, Schema(), out var plan));
        Assert.IsTrue(plan!.RequiresExecutionBudget);
        Assert.IsTrue(plan.TryResolveStart(new Dictionary<string, JsonElement>(), out var transition));
        Assert.IsInstanceOfType<WorkflowCollaborationWaitPlan>(transition.AutomaticWait);
        Assert.IsFalse(transition.CompletesInstance);
    }
}
