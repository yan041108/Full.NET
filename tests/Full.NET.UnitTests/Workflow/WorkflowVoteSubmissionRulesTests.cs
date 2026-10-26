using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>投票证据覆盖选择和私有答案，旧待办不借用后任内容。</summary>
[TestClass]
public sealed class WorkflowVoteSubmissionRulesTests
{
    private static readonly WorkflowFormSchema Schema = new(1, 1,
        [new("main", [new("reason", "text", true, new Dictionary<string, JsonElement>()),
            new("confirmed", "switch", false, new Dictionary<string, JsonElement>())])]);

    [TestMethod]
    public void Evidence_binds_choice_and_answers_but_ignores_property_order()
    {
        var yes = Compile("""{"reason":"reviewed","confirmed":true}""", "yes");
        var reordered = Compile("""{"confirmed":true,"reason":"reviewed"}""", "yes");
        var no = Compile("""{"reason":"reviewed","confirmed":true}""", "no");
        Assert.AreEqual(yes.EvidenceHash, reordered.EvidenceHash);
        Assert.AreEqual(yes.ValuesJson, no.ValuesJson);
        Assert.AreNotEqual(yes.EvidenceHash, no.EvidenceHash);
        using var evidence = JsonDocument.Parse(yes.EvidenceJson);
        Assert.AreEqual("yes", evidence.RootElement.GetProperty("choiceKey").GetString());
        Assert.AreEqual("reviewed", evidence.RootElement.GetProperty("values").GetProperty("reason").GetString());
    }

    [TestMethod]
    [DataRow("{}", "yes", true)]
    [DataRow("{\"reason\":\"a\",\"reason\":\"b\"}", "yes", true)]
    [DataRow("{\"reason\":\"a\",\"foreign\":true}", "yes", true)]
    [DataRow("{\"reason\":\"a\",\"confirmed\":\"true\"}", "yes", true)]
    [DataRow("{\"reason\":\"a\"}", "approve", true)]
    [DataRow("{\"reason\":\"a\"}", "abstain", false)]
    public void Invalid_or_ambiguous_submission_is_rejected(string json, string choice, bool allowAbstain)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowVoteSubmissionRules.TryCompile(Schema, document.RootElement, choice, allowAbstain, out _));
    }

    [TestMethod]
    public void Personal_history_reads_only_the_provided_todo_view()
    {
        var successor = Compile("""{"reason":"successor-secret"}""", "no");
        Assert.IsTrue(WorkflowVoteSubmissionRules.TryReadView(new("{}", 1, null, null), Schema, true, out var original));
        Assert.IsFalse(original!.Submitted);
        Assert.AreEqual(0, original.Values.EnumerateObject().Count());
        Assert.IsTrue(WorkflowVoteSubmissionRules.TryReadView(new("{}", 2, successor.EvidenceJson, 3), Schema, true, out var submitted));
        Assert.AreEqual("successor-secret", submitted!.Values.GetProperty("reason").GetString());
        Assert.AreEqual(3L, submitted.Revision);
        Assert.AreEqual("no", submitted.ChoiceKey);
        Assert.AreEqual(0, original.Values.EnumerateObject().Count());
    }

    [TestMethod]
    [DataRow("{}", 1L, null, 2L)]
    [DataRow("{}", 1L, "{}", null)]
    [DataRow("{}", 2L, "{\"choiceKey\":\"yes\",\"values\":{\"reason\":\"a\"}}", 2L)]
    [DataRow("{\"reason\":\"implicit-prefill\"}", 1L, null, null)]
    [DataRow("{}", 1L, "{\"choiceKey\":\"yes\",\"values\":{}}", 2L)]
    public void Corrupt_private_views_fail_closed(string input, long revision, string? output, long? outputRevision) =>
        Assert.IsFalse(WorkflowVoteSubmissionRules.TryReadView(new(input, revision, output, outputRevision), Schema, true, out _));

    [TestMethod]
    public void Receipt_hash_binds_actor_todo_revisions_and_choice()
    {
        var todo = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var yes = Compile("""{"reason":"a"}""", "yes");
        var baseline = WorkflowVoteSubmissionRules.HashRequest(todo, actor, 1, 2, yes, " comment ", null);
        Assert.AreEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, actor, 1, 2, yes, "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, Guid.CreateVersion7(), 1, 2, yes, "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(Guid.CreateVersion7(), actor, 1, 2, yes, "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, actor, 2, 2, yes, "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, actor, 1, 3, yes, "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, actor, 1, 2, Compile("""{"reason":"a"}""", "no"), "comment", null));
        Assert.AreNotEqual(baseline, WorkflowVoteSubmissionRules.HashRequest(todo, actor, 1, 2, yes, "comment", Guid.CreateVersion7()));
    }

    private static WorkflowVoteSubmissionData Compile(string json, string choice)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsTrue(WorkflowVoteSubmissionRules.TryCompile(Schema, document.RootElement, choice, true, out var submission));
        return submission!;
    }
}
