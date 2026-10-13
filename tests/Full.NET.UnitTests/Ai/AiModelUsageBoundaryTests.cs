using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>供应商回执的缓存/推理/总量与输入输出必须一致，不用无效字段释放原预算。</summary>
internal static class PaidReceiptCases
{
    internal static UsageDetails Usage(int fault, bool embedding = false)
    {
        var u = new UsageDetails { InputTokenCount = 7, OutputTokenCount = embedding ? 0 : 3 };
        switch (fault)
        {
            case 0: u.InputTokenCount = AiExecutionBudget.MaximumTokens + 1; break;
            case 1: u.CachedInputTokenCount = -1; break;
            case 2: u.CachedInputTokenCount = 8; break;
            case 3: u.InputTokenCount = null; u.CachedInputTokenCount = 1; break;
            case 4: u.ReasoningTokenCount = -1; break;
            case 5: u.ReasoningTokenCount = 4; break;
            case 6: u.OutputTokenCount = null; u.ReasoningTokenCount = 1; break;
            case 7: u.TotalTokenCount = -1; break;
            case 8: u.TotalTokenCount = 2 * AiExecutionBudget.MaximumTokens + 1; break;
            case 9: u.TotalTokenCount = 1; break;
            case 10: u.InputTokenCount = -1; break;
            case 11: u.OutputTokenCount = -1; break;
            case 12: u.OutputTokenCount = null; u.TotalTokenCount = 1; break;
            case 13: u.InputTokenCount = null; u.TotalTokenCount = 1; break;
        }
        return u;
    }
}

[TestClass]
public sealed class AiModelUsageBoundaryTests
{
    [TestMethod] [DataRow(false, 0)] [DataRow(false, 1)] [DataRow(false, 2)] [DataRow(false, 3)] [DataRow(false, 4)] [DataRow(false, 5)] [DataRow(false, 6)] [DataRow(false, 7)] [DataRow(false, 8)] [DataRow(false, 9)] [DataRow(false, 10)] [DataRow(false, 11)] [DataRow(true, 0)] [DataRow(true, 1)] [DataRow(true, 2)] [DataRow(true, 3)] [DataRow(true, 4)] [DataRow(true, 5)] [DataRow(true, 6)] [DataRow(true, 7)] [DataRow(true, 8)] [DataRow(true, 9)] [DataRow(true, 10)] [DataRow(true, 11)] [DataRow(false, 12)] [DataRow(false, 13)] [DataRow(true, 12)] [DataRow(true, 13)]
    public async Task Paid_receipt_agent_rejects_invalid_metadata_and_blocks_next_dispatch(bool streaming, int fault)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "正文")) { Usage = PaidReceiptCases.Usage(fault) });
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Updates(PaidReceiptCases.Usage(fault)));
        var client = new AiRunOutputLimitClient(inner, 100);
        var error = await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(async () =>
        { if (streaming) { await foreach (var _ in client.GetStreamingResponseAsync([])) { } } else await client.GetResponseAsync([]); });
        Assert.IsTrue(error.DispatchStarted); Assert.AreEqual("ai.budget.invalid_usage", error.Code);
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.AreEqual(1, inner.ReceivedCalls().Count(c => c.GetMethodInfo().Name is "GetResponseAsync" or "GetStreamingResponseAsync"));
    }

    [TestMethod]
    public async Task Paid_receipt_agent_current_cancellation_has_no_late_update()
    {
        using var token = new CancellationTokenSource(); var inner = Substitute.For<IChatClient>();
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(new PaidCancellingUpdates(token));
        var client = new AiRunOutputLimitClient(inner, 100); var updates = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { await foreach (var _ in client.GetStreamingResponseAsync([], cancellationToken: token.Token)) updates++; });
        Assert.AreEqual(0, updates); Assert.ThrowsExactly<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
    }
    [TestMethod] [DataRow("response")] [DataRow("source")] [DataRow("enumerator")]
    public async Task Paid_receipt_agent_null_provider_results_are_budget_failures(string fault)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns((ChatResponse)null!);
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(fault == "source" ? null! : new MissingEnumerator());
        var client = new AiRunOutputLimitClient(inner, 100);
        var error = await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(async () =>
        { if (fault == "response") await client.GetResponseAsync([]); else await foreach (var _ in client.GetStreamingResponseAsync([])) { } });
        Assert.AreEqual("ai.budget.invalid_usage", error.Code); Assert.IsTrue(error.DispatchStarted);
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
    }
    [TestMethod]
    public async Task Paid_receipt_agent_stream_global_input_bound_applies_without_optional_limit()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(OverflowUpdates());
        var client = new AiRunOutputLimitClient(inner, 100); var seen = 0;
        await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(async () => { await foreach (var _ in client.GetStreamingResponseAsync([])) seen++; });
        Assert.AreEqual(1, seen); Assert.ThrowsExactly<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
    }
    private sealed class MissingEnumerator : IAsyncEnumerable<ChatResponseUpdate>
    { public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken token = default) => null!; }
    private static async IAsyncEnumerable<ChatResponseUpdate> OverflowUpdates()
    { await Task.CompletedTask; for (var i = 0; i < 2; i++) yield return new() { Contents = [new UsageContent(new() { InputTokenCount = 800_000_000, OutputTokenCount = 0 })] }; }
    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(UsageDetails usage)
    { await Task.CompletedTask; yield return new() { Contents = [new TextContent("正文"), new UsageContent(usage)] }; }
}

public sealed partial class AiEmbeddingTestBoundaryTests
{
    [TestMethod] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)] [DataRow(10)] [DataRow(11)]
    public async Task Paid_receipt_manager_embedding_rejects_invalid_metadata(int fault)
    {
        using var f = new Fixture(); f.Returned = new([new(new float[] { 1 })]) { Usage = PaidReceiptCases.Usage(fault, true) };
        var result = await f.Service.TestAsync(f.Model.Id, new("x", null), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(1, f.Dispatches);
        Assert.AreEqual(new AiOperationUsage(null, null), f.Settlements.Single().Usage);
    }
}

public sealed partial class AiKnowledgeIndexEmbeddingTests
{
    [TestMethod] [DataRow(false, 0)] [DataRow(false, 1)] [DataRow(false, 2)] [DataRow(false, 3)] [DataRow(false, 4)] [DataRow(false, 5)] [DataRow(false, 6)] [DataRow(false, 7)] [DataRow(false, 8)] [DataRow(false, 9)] [DataRow(false, 10)] [DataRow(false, 11)] [DataRow(true, 0)] [DataRow(true, 1)] [DataRow(true, 2)] [DataRow(true, 3)] [DataRow(true, 4)] [DataRow(true, 5)] [DataRow(true, 6)] [DataRow(true, 7)] [DataRow(true, 8)] [DataRow(true, 9)] [DataRow(true, 10)] [DataRow(true, 11)]
    public async Task Paid_receipt_text_index_rejects_invalid_metadata(bool scoped, int fault)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = PaidReceiptCases.Usage(fault, true);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(null, null), "failed", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Generator.Calls);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Paid_receipt_text_index_preserves_valid_cached_usage(bool scoped)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = new() { InputTokenCount = 7, OutputTokenCount = 0, CachedInputTokenCount = 3, TotalTokenCount = 7 };
        Assert.IsNotNull(await f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(7, 0, 3), "succeeded", Arg.Any<CancellationToken>());
    }
}

public sealed partial class AiKnowledgeIndexPdfPlanningServiceTests
{
    [TestMethod] [DataRow(false, 0)] [DataRow(false, 1)] [DataRow(false, 2)] [DataRow(false, 3)] [DataRow(false, 4)] [DataRow(false, 5)] [DataRow(false, 6)] [DataRow(false, 7)] [DataRow(false, 8)] [DataRow(false, 9)] [DataRow(false, 10)] [DataRow(false, 11)] [DataRow(true, 0)] [DataRow(true, 1)] [DataRow(true, 2)] [DataRow(true, 3)] [DataRow(true, 4)] [DataRow(true, 5)] [DataRow(true, 6)] [DataRow(true, 7)] [DataRow(true, 8)] [DataRow(true, 9)] [DataRow(true, 10)] [DataRow(true, 11)]
    public async Task Paid_receipt_pdf_index_rejects_invalid_metadata(bool scoped, int fault)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = PaidReceiptCases.Usage(fault, true);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(null, null), "failed", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Generator.Calls);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Paid_receipt_pdf_index_preserves_valid_cached_usage(bool scoped)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = new() { InputTokenCount = 7, OutputTokenCount = 0, CachedInputTokenCount = 3, TotalTokenCount = 7 };
        Assert.IsNotNull(await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(7, 0, 3), "succeeded", Arg.Any<CancellationToken>());
    }
}

public sealed partial class AiKnowledgeIndexDocxPlanningServiceTests
{
    [TestMethod] [DataRow(false, 0)] [DataRow(false, 1)] [DataRow(false, 2)] [DataRow(false, 3)] [DataRow(false, 4)] [DataRow(false, 5)] [DataRow(false, 6)] [DataRow(false, 7)] [DataRow(false, 8)] [DataRow(false, 9)] [DataRow(false, 10)] [DataRow(false, 11)] [DataRow(true, 0)] [DataRow(true, 1)] [DataRow(true, 2)] [DataRow(true, 3)] [DataRow(true, 4)] [DataRow(true, 5)] [DataRow(true, 6)] [DataRow(true, 7)] [DataRow(true, 8)] [DataRow(true, 9)] [DataRow(true, 10)] [DataRow(true, 11)]
    public async Task Paid_receipt_docx_index_rejects_invalid_metadata(bool scoped, int fault)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = PaidReceiptCases.Usage(fault, true);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(null, null), "failed", Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Generator.Calls);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Paid_receipt_docx_index_preserves_valid_cached_usage(bool scoped)
    {
        using var f = new Fixture(scoped); f.Generator.UsageOverride = new() { InputTokenCount = 7, OutputTokenCount = 0, CachedInputTokenCount = 3, TotalTokenCount = 7 };
        Assert.IsNotNull(await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(7, 0, 3), "succeeded", Arg.Any<CancellationToken>());
    }
}

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false, false, 0)] [DataRow(false, false, 1)] [DataRow(false, false, 2)] [DataRow(false, false, 3)] [DataRow(false, false, 4)] [DataRow(false, false, 5)] [DataRow(false, false, 6)] [DataRow(false, false, 7)] [DataRow(false, false, 8)] [DataRow(false, false, 9)] [DataRow(false, false, 10)] [DataRow(false, false, 11)] [DataRow(false, true, 0)] [DataRow(false, true, 1)] [DataRow(false, true, 2)] [DataRow(false, true, 3)] [DataRow(false, true, 4)] [DataRow(false, true, 5)] [DataRow(false, true, 6)] [DataRow(false, true, 7)] [DataRow(false, true, 8)] [DataRow(false, true, 9)] [DataRow(false, true, 10)] [DataRow(false, true, 11)] [DataRow(true, false, 0)] [DataRow(true, false, 1)] [DataRow(true, false, 2)] [DataRow(true, false, 3)] [DataRow(true, false, 4)] [DataRow(true, false, 5)] [DataRow(true, false, 6)] [DataRow(true, false, 7)] [DataRow(true, false, 8)] [DataRow(true, false, 9)] [DataRow(true, false, 10)] [DataRow(true, false, 11)] [DataRow(true, true, 0)] [DataRow(true, true, 1)] [DataRow(true, true, 2)] [DataRow(true, true, 3)] [DataRow(true, true, 4)] [DataRow(true, true, 5)] [DataRow(true, true, 6)] [DataRow(true, true, 7)] [DataRow(true, true, 8)] [DataRow(true, true, 9)] [DataRow(true, true, 10)] [DataRow(true, true, 11)]
    public async Task Paid_receipt_knowledge_answer_rejects_invalid_metadata(bool scoped, bool embedding, int fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(scoped);
        if (embedding) f.Factory.EmbeddingUsage = PaidReceiptCases.Usage(fault, true); else f.Factory.Usage = PaidReceiptCases.Usage(fault);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(new AiOperationUsage(null, null), f.Budget.Settlements.Last().Usage);
        Assert.AreEqual("failed", f.Budget.Settlements.Last().Outcome);
    }
}
