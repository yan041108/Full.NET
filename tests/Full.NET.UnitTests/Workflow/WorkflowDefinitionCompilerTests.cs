using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowDefinitionCompilerTests
{
    [TestMethod]
    public void Compile_produces_stable_canonical_json_and_hash_for_reordered_object_keys()
    {
        var first = CompileValid("{\"label\":\"审批\",\"nextNodeKeys\":[\"end\"]}");
        var second = CompileValid("{\"nextNodeKeys\":[\"end\"],\"label\":\"审批\"}");

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value!.CanonicalJson, second.Value!.CanonicalJson);
        Assert.AreEqual(first.Value.ContentHash, second.Value.ContentHash);
    }

    [TestMethod]
    [DataRow("unknown", WorkflowErrorCodes.DefinitionNodeTypeUnknown)]
    [DataRow("duplicate", WorkflowErrorCodes.DefinitionNodeKeyDuplicate)]
    [DataRow("dangling", WorkflowErrorCodes.DefinitionReferenceDangling)]
    [DataRow("unreachable", WorkflowErrorCodes.DefinitionNodeUnreachable)]
    [DataRow("no-end", WorkflowErrorCodes.DefinitionEndMissing)]
    [DataRow("back-edge", WorkflowErrorCodes.DefinitionBackEdgeIllegal)]
    [DataRow("definition-schema", WorkflowErrorCodes.DefinitionSchemaUnsupported)]
    [DataRow("node-schema", WorkflowErrorCodes.DefinitionSchemaUnsupported)]
    public void Compile_rejects_invalid_graphs_with_stable_error_codes(
        string scenario,
        string expectedCode)
    {
        var result = WorkflowDefinitionCompiler.Compile(CreateInvalid(scenario));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(expectedCode, result.ErrorCode);
    }

    [TestMethod]
    public void Compile_with_form_schema_materializes_complete_node_field_policies()
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval",
                "{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"secret\":\"hidden\",\"reason\":\"readOnly\"}}"),
            Node("end", "end", "{}"),
        ]);
        var schema = FormSchema(
            FormField("reason", required: false),
            FormField("secret", required: false),
            FormField("decision", required: true));

        var result = WorkflowDefinitionCompiler.Compile(draft, schema);

        Assert.IsTrue(result.IsSuccess);
        using var document = JsonDocument.Parse(result.Value!.CanonicalJson);
        var approval = document.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("nodeKey").GetString() == "approve");
        var policies = approval.GetProperty("config").GetProperty("fieldPolicies");
        Assert.AreEqual("required", policies.GetProperty("decision").GetString());
        Assert.AreEqual("readOnly", policies.GetProperty("reason").GetString());
        Assert.AreEqual("hidden", policies.GetProperty("secret").GetString());
    }

    [TestMethod]
    public void Compile_accepts_exclusive_gateway_bound_to_form_schema()
    {
        var result = WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"route\"]}"),
            Node("route", "gateway.exclusive", "{\"nextNodeKeys\":[\"large\",\"normal\"],\"branches\":[{\"branchKey\":\"large\",\"nextNodeKey\":\"large\",\"condition\":{\"fieldKey\":\"amount\",\"operator\":\"greaterThanOrEqual\",\"value\":\"1000.00\"}}],\"defaultNextNodeKey\":\"normal\"}"),
            Node("large", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("normal", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]), MoneyFormSchema());

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Compile_rejects_gateway_condition_that_is_not_bound_to_form_schema()
    {
        var result = WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"route\"]}"),
            Node("route", "gateway.exclusive", "{\"nextNodeKeys\":[\"large\",\"normal\"],\"branches\":[{\"branchKey\":\"large\",\"nextNodeKey\":\"large\",\"condition\":{\"fieldKey\":\"missing\",\"operator\":\"equals\",\"value\":\"1000.00\"}}],\"defaultNextNodeKey\":\"normal\"}"),
            Node("large", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("normal", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]), MoneyFormSchema());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionGatewayInvalid, result.ErrorCode);
    }

    [TestMethod]
    public void Compile_accepts_cc_with_closed_recipient_identifiers()
    {
        var recipientA = Guid.NewGuid();
        var recipientB = Guid.NewGuid();
        var result = WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"copy\"]}"),
            Node("copy", "notify.cc", JsonSerializer.Serialize(new
            {
                nextNodeKeys = new[] { "approve" },
                recipientUserIds = new[] { recipientA, recipientB },
            })),
            Node("approve", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]));

        Assert.IsTrue(result.IsSuccess);
    }

    /// <summary>发布编译必须拒绝结构不闭合的多人审批策略。</summary>
    [TestMethod]
    public void Compile_rejects_invalid_multi_approval_policy()
    {
        var user = Guid.CreateVersion7();
        var result = WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", JsonSerializer.Serialize(new
            {
                nextNodeKeys = new[] { "end" },
                approvalPolicy = new
                {
                    modeKey = "all",
                    approverUserIds = new[] { user, user },
                },
            })),
            Node("end", "end", "{}"),
        ]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionApprovalPolicyInvalid, result.ErrorCode);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("[\"not-a-guid\"]")]
    [DataRow("[\"00000000-0000-0000-0000-000000000000\"]")]
    public void Compile_rejects_invalid_cc_recipient_configuration(string recipientsJson)
    {
        var result = WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"copy\"]}"),
            Node("copy", "notify.cc",
                $"{{\"nextNodeKeys\":[\"approve\"],\"recipientUserIds\":{recipientsJson}}}"),
            Node("approve", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("workflow.definition.cc_recipients_invalid", result.ErrorCode);
    }

    [TestMethod]
    public void Compile_rejects_duplicate_or_over_limit_cc_recipients()
    {
        var duplicate = Guid.NewGuid();
        var duplicateResult = CompileCcRecipients([duplicate, duplicate]);
        var overLimitResult = CompileCcRecipients(
            Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray());

        Assert.AreEqual("workflow.definition.cc_recipients_invalid", duplicateResult.ErrorCode);
        Assert.AreEqual("workflow.definition.cc_recipients_invalid", overLimitResult.ErrorCode);
    }

    [TestMethod]
    [DataRow("no-approval")]
    [DataRow("branch")]
    public void Compile_rejects_graph_shapes_that_the_current_runtime_cannot_execute(string scenario)
    {
        var result = WorkflowDefinitionCompiler.Compile(CreateTopology(scenario));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionTopologyUnsupported, result.ErrorCode);
    }

    [TestMethod]
    public void Compile_accepts_a_linear_multi_approval_topology()
    {
        var result = WorkflowDefinitionCompiler.Compile(CreateTopology("multiple-approvals"));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value?.CanonicalJson);
    }

    /// <summary>发布时应把审批节点的闭合超时策略写入不可变规范定义。</summary>
    [TestMethod]
    public void Compile_materializes_closed_todo_timeout_policy()
    {
        var escalationRecipient = Guid.CreateVersion7();
        var result = CompileValid(JsonSerializer.Serialize(new
        {
            nextNodeKeys = new[] { "end" },
            timeoutPolicy = new
            {
                dueAfterMinutes = 60,
                reminderIntervalMinutes = 15,
                maxReminderCount = 2,
                escalationAfterMinutes = 120,
                escalationRecipientUserId = escalationRecipient,
            },
        }));

        Assert.IsTrue(result.IsSuccess);
        using var document = JsonDocument.Parse(result.Value!.CanonicalJson);
        var policy = document.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("nodeKey").GetString() == "approve")
            .GetProperty("config").GetProperty("timeoutPolicy");
        Assert.AreEqual(60, policy.GetProperty("dueAfterMinutes").GetInt32());
        Assert.AreEqual(2, policy.GetProperty("maxReminderCount").GetInt32());
        Assert.AreEqual(escalationRecipient.ToString("D"),
            policy.GetProperty("escalationRecipientUserId").GetString());
    }

    /// <summary>非法或开放式策略必须在发布边界失败关闭。</summary>
    [TestMethod]
    [DataRow("{\"dueAfterMinutes\":0,\"reminderIntervalMinutes\":15,\"maxReminderCount\":1}")]
    [DataRow("{\"dueAfterMinutes\":60,\"reminderIntervalMinutes\":15,\"maxReminderCount\":1,\"unknown\":true}")]
    [DataRow("{\"dueAfterMinutes\":60,\"reminderIntervalMinutes\":15,\"maxReminderCount\":1,\"escalationAfterMinutes\":30,\"escalationRecipientUserId\":\"not-a-guid\"}")]
    public void Compile_rejects_invalid_todo_timeout_policy(string policyJson)
    {
        var result = CompileValid($"{{\"nextNodeKeys\":[\"end\"],\"timeoutPolicy\":{policyJson}}}");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionTimeoutPolicyInvalid, result.ErrorCode);
    }

    [TestMethod]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"missing\":\"editable\"}}")]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":{\"reason\":\"ownerOnly\"}}")]
    [DataRow("{\"nextNodeKeys\":[\"end\"],\"fieldPolicies\":[]}")]
    public void Compile_rejects_unknown_or_malformed_node_field_policies(string approvalConfig)
    {
        var result = WorkflowDefinitionCompiler.Compile(
            new WorkflowDefinitionDraft(1,
            [
                Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
                Node("approve", "human.approval", approvalConfig),
                Node("end", "end", "{}"),
            ]),
            FormSchema(FormField("reason", required: false)));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionFieldPolicyInvalid, result.ErrorCode);
    }

    private static WorkflowCompilationResult CompileValid(string approvalConfig) =>
        WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", approvalConfig),
            Node("end", "end", "{}"),
        ]));

    private static WorkflowCompilationResult CompileCcRecipients(IReadOnlyList<Guid> recipients) =>
        WorkflowDefinitionCompiler.Compile(new WorkflowDefinitionDraft(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"copy\"]}"),
            Node("copy", "notify.cc", JsonSerializer.Serialize(new
            {
                nextNodeKeys = new[] { "approve" },
                recipientUserIds = recipients,
            })),
            Node("approve", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]));

    private static WorkflowDefinitionDraft CreateInvalid(string scenario) => scenario switch
    {
        "unknown" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"mystery\"]}"),
            Node("mystery", "script", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "duplicate" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("start", "end", "{}"),
        ]),
        "dangling" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"missing\"]}"),
            Node("end", "end", "{}"),
        ]),
        "unreachable" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("orphan", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "no-end" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", "{}"),
        ]),
        "back-edge" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"approve\"]}"),
            Node("approve", "human.approval", "{\"nextNodeKeys\":[\"start\",\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "definition-schema" => new(2,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "node-schema" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}") with { NodeSchemaVersion = 2 },
            Node("end", "end", "{}"),
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static WorkflowDefinitionDraft CreateTopology(string scenario) => scenario switch
    {
        "no-approval" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "multiple-approvals" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"first\"]}"),
            Node("first", "human.approval", "{\"nextNodeKeys\":[\"second\"]}"),
            Node("second", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        "branch" => new(1,
        [
            Node("start", "start", "{\"nextNodeKeys\":[\"first\",\"second\"]}"),
            Node("first", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("second", "human.approval", "{\"nextNodeKeys\":[\"end\"]}"),
            Node("end", "end", "{}"),
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static WorkflowNodeDraft Node(string key, string type, string json) =>
        new(key, type, 1, JsonDocument.Parse(json).RootElement.Clone());

    private static WorkflowFormSchema FormSchema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField FormField(string key, bool required) =>
        new(key, "text", required, new Dictionary<string, JsonElement>());

    private static WorkflowFormSchema MoneyFormSchema() =>
        new(1, 1,
        [
            new WorkflowFormSection("main",
            [
                new WorkflowFormField(
                    "amount",
                    "money",
                    true,
                    new Dictionary<string, JsonElement>
                    {
                        ["scale"] = JsonSerializer.SerializeToElement(2),
                    }),
            ]),
        ]);
}
