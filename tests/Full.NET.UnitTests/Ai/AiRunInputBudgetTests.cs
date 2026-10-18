using System.Runtime.CompilerServices;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>输入按回执累计；流的部分回执与未知结果不能产生新的输入额度。</summary>
[TestClass]
public sealed class AiRunInputBudgetTests
{
    [TestMethod]
    public async Task Invalid_concurrent_stream_disposal_keeps_the_inflight_budget_locked()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<ChatResponseUpdate> WaitForRelease()
        {
            entered.TrySetResult(); await release.Task;
            yield return new() { Contents = [new TextContent("frame")] };
        }
        var client = new AiRunOutputLimitClient(StreamingClient(WaitForRelease()), 100);
        var enumerator = client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 }).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask(); await entered.Task;
        try
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => enumerator.DisposeAsync().AsTask());
            var error = await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
            Assert.AreEqual("ai.budget.operation_conflict", error.Code);
        }
        finally { release.TrySetResult(); await pending; await enumerator.DisposeAsync(); }
    }

    [TestMethod]
    public async Task Delayed_old_stream_disposal_cannot_unlock_a_new_inflight_request()
    {
        var pending = new TaskCompletionSource<ChatResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = StreamingClient(DisposalUpdates(() => { }));
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var client = new AiRunOutputLimitClient(inner, 100);
        var previous = client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 }).GetAsyncEnumerator();
        while (await previous.MoveNextAsync()) { }
        var current = client.GetResponseAsync([], new() { MaxOutputTokens = 10 });
        try
        {
            await previous.DisposeAsync(); await previous.DisposeAsync();
            var error = await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
            Assert.AreEqual("ai.budget.operation_conflict", error.Code);
        }
        finally { pending.TrySetResult(Response(0)); await current; }
    }

    [TestMethod]
    public async Task Disposing_before_first_move_closes_scope_captured_by_provider_factory()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        AiChatInputBudgetScope? captured = null;
        var inner = Client(() => Response(0));
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { captured = AiChatInputBudgetScope.RequireCurrent(); return ScopedUpdates(captured, []); });
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        await client.GetStreamingResponseAsync([]).GetAsyncEnumerator().DisposeAsync();
        Assert.IsNotNull(captured); Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => captured.RevalidateAsync(default));
        var error = await Assert.ThrowsExactlyAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.IsFalse(error.DispatchStarted);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Streaming_provider_keeps_one_budget_scope_through_factory_yields_and_disposal(bool early)
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        AiChatInputBudgetScope? captured = null; var checks = new List<string>();
        var inner = Client(() => Response(0));
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { captured = AiChatInputBudgetScope.RequireCurrent(); return ScopedUpdates(captured, checks); });
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100, consumedInputTokens: 90);
        var stream = client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 });
        Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent()); Assert.IsNotNull(captured); Assert.AreEqual(10L, captured.InputTokenLimit);
        await using (var enumerator = stream.GetAsyncEnumerator())
        {
            Assert.IsTrue(await enumerator.MoveNextAsync()); Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
            if (!early) { Assert.IsTrue(await enumerator.MoveNextAsync()); Assert.IsFalse(await enumerator.MoveNextAsync()); }
        }
        Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent()); Assert.AreEqual("dispose", checks[^1]);
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => captured.RevalidateAsync(default));
    }

    [TestMethod]
    public async Task Tighter_caller_context_cannot_prevent_owned_stream_cleanup_or_be_overwritten()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        var cleaned = false; var inner = StreamingClient(DisposalUpdates(() => cleaned = true));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        var enumerator = client.GetStreamingResponseAsync([]).GetAsyncEnumerator();
        Assert.IsTrue(await enumerator.MoveNextAsync());
        using var tighter = AiChatInputBudgetScope.Begin(5);
        await enumerator.DisposeAsync();
        Assert.IsTrue(cleaned); Assert.AreSame(tighter, AiChatInputBudgetScope.RequireCurrent());
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ScopedUpdates(AiChatInputBudgetScope captured, List<string> checks)
    {
        try
        {
            for (var i = 0; i < 2; i++)
            {
                await Task.Yield(); Assert.AreSame(captured, AiChatInputBudgetScope.RequireCurrent()); await captured.RevalidateAsync(default);
                checks.Add("move"); yield return new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0 })] };
            }
        }
        finally { Assert.AreSame(captured, AiChatInputBudgetScope.RequireCurrent()); checks.Add("dispose"); }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> DisposalUpdates(Action cleaned)
    {
        try { await Task.Yield(); yield return new() { Contents = [new TextContent("frame")] }; }
        finally { cleaned(); }
    }

    [TestMethod]
    public async Task Known_input_is_cumulative_and_exact_exhaustion_blocks_next_dispatch()
    {
        var calls = 0;
        var inner = Client(() => Response(++calls == 1 ? 30 : 70));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        await client.GetResponseAsync([]);
        await client.GetResponseAsync([]);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.IsFalse(error.DispatchStarted);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task Missing_input_receipt_preserves_output_but_blocks_next_model()
    {
        var inner = Client(() => Response(null));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        Assert.IsNull((await client.GetResponseAsync([])).Usage!.InputTokenCount);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.IsFalse(error.DispatchStarted);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(-1L)] [DataRow(101L)] [DataRow(long.MaxValue)]
    public async Task Invalid_input_receipt_closes_budget_and_is_classified_as_dispatched(long input)
    {
        var inner = Client(() => Response(input));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.IsTrue(error.DispatchStarted);
        Assert.AreEqual("ai.budget.invalid_usage", error.Code);
        await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Explicit_zero_input_does_not_consume_extra_tokens()
    {
        var inner = Client(() => Response(0));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 1);
        await client.GetResponseAsync([]);
        await client.GetResponseAsync([]);
        await inner.Received(2).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(null)] [DataRow(100L)]
    public async Task Unknown_or_exhausted_history_never_calls_provider(long? consumed)
    {
        var inner = Client(() => Response(0));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100, consumedInputTokens: consumed);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        Assert.IsFalse(error.DispatchStarted);
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(0L, 0L)] [DataRow(-1L, 0L)] [DataRow(1000000001L, 0L)] [DataRow(100L, -1L)] [DataRow(100L, 101L)]
    public void Invalid_limits_or_history_reject_before_dispatch(long limit, long used)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiRunOutputLimitClient(Client(() => Response(0)), 100,
            inputTokenLimit: limit, consumedInputTokens: used));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Stream_reserves_all_remaining_input_even_with_zero_or_missing_usage(bool unknown)
    {
        var inner = StreamingClient(Updates(unknown ? null : 0));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        await foreach (var _ in client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 })) { }
        await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(6L, 5L)] [DataRow(0L, 11L)] [DataRow(0L, long.MaxValue)]
    public async Task Stream_input_deltas_cannot_exceed_reserved_remaining_input(long first, long second)
    {
        var inner = StreamingClient(Updates(first, second));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100, consumedInputTokens: 90);
        var delivered = 0;
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 })) delivered++;
        });
        Assert.AreEqual(1, delivered);
        Assert.IsTrue(error.DispatchStarted);
        await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
    }

    [TestMethod]
    public async Task Early_stream_disposal_never_refunds_partial_input_receipt()
    {
        var inner = StreamingClient(Updates(0, 0));
        var client = new AiRunOutputLimitClient(inner, 100, inputTokenLimit: 100);
        await using (var stream = client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 10 }).GetAsyncEnumerator())
            Assert.IsTrue(await stream.MoveNextAsync());
        await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync([]));
    }

    private static IChatClient Client(Func<ChatResponse> response)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(_ => response());
        return inner;
    }

    private static IChatClient StreamingClient(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        var inner = Client(() => Response(0));
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(stream);
        return inner;
    }

    private static ChatResponse Response(long? input) => new(new ChatMessage(ChatRole.Assistant, "ok"))
        { Usage = new() { InputTokenCount = input, OutputTokenCount = 0 } };

    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(long? first, long? second = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = first, OutputTokenCount = 0 })] };
        if (second.HasValue) yield return new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = second, OutputTokenCount = 0 })] };
    }
}
