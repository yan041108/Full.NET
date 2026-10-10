using System.Runtime.CompilerServices;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>模型调用共用运行输出额度；未知结果与流式枚举不能免费补回额度。</summary>
[TestClass]
public sealed class AiRunOutputBudgetTests
{
    [TestMethod]
    public async Task Stream_current_failure_closes_budget_and_disposes_source_once()
    {
        var iterator = Substitute.For<IAsyncEnumerator<ChatResponseUpdate>>();
        iterator.MoveNextAsync().Returns(new ValueTask<bool>(true));
        iterator.Current.Returns(_ => throw new IOException("unknown response"));
        var source = Substitute.For<IAsyncEnumerable<ChatResponseUpdate>>();
        source.GetAsyncEnumerator(Arg.Any<CancellationToken>()).Returns(iterator);
        var inner = Client((_, _) => Task.FromResult(Response(1)));
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(source);
        var client = new AiRunOutputLimitClient(inner, 100);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 40 })) { }
        });
        await iterator.Received(1).DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Known_usage_reduces_next_request_and_preserves_original_options()
    {
        var limits = new List<int?>();
        var inner = Client((options, _) => { limits.Add(options!.MaxOutputTokens); return Task.FromResult(Response(30)); });
        var client = new AiRunOutputLimitClient(inner, 100);
        var options = new ChatOptions { MaxOutputTokens = 90 };
        await client.GetResponseAsync([], options);
        await client.GetResponseAsync([], options);
        CollectionAssert.AreEqual(new int?[] { 90, 70 }, limits);
        Assert.AreEqual(90, options.MaxOutputTokens);
    }

    [TestMethod]
    public async Task Exhausted_budget_rejects_before_another_provider_call()
    {
        var inner = Client((_, _) => Task.FromResult(Response(100)));
        var client = new AiRunOutputLimitClient(inner, 100);
        await client.GetResponseAsync([]);
        var rejection = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        Assert.IsFalse(((AiRunOutputBudgetException)rejection).DispatchStarted);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Unknown_usage_keeps_entire_request_reservation()
    {
        var limits = new List<int?>();
        var inner = Client((options, _) => { limits.Add(options!.MaxOutputTokens); return Task.FromResult(Response(null)); });
        var client = new AiRunOutputLimitClient(inner, 100);
        await client.GetResponseAsync([], new() { MaxOutputTokens = 40 });
        await client.GetResponseAsync([]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        CollectionAssert.AreEqual(new int?[] { 40, 60 }, limits);
    }

    [TestMethod]
    [DataRow(-1L)] [DataRow(41L)] [DataRow(long.MaxValue)]
    public async Task Invalid_reported_usage_rejects_delivery_and_poisoned_budget(long output)
    {
        var inner = Client((_, _) => Task.FromResult(Response(output)));
        var client = new AiRunOutputLimitClient(inner, 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([], new() { MaxOutputTokens = 40 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Explicit_zero_refunds_reserved_output()
    {
        var limits = new List<int?>();
        var inner = Client((options, _) => { limits.Add(options!.MaxOutputTokens); return Task.FromResult(Response(0)); });
        var client = new AiRunOutputLimitClient(inner, 100);
        await client.GetResponseAsync([]);
        await client.GetResponseAsync([]);
        CollectionAssert.AreEqual(new int?[] { 100, 100 }, limits);
    }

    [TestMethod]
    public async Task Unknown_provider_failure_never_authorizes_retry()
    {
        var inner = Client((_, _) => Task.FromException<ChatResponse>(new IOException("provider failed")));
        var client = new AiRunOutputLimitClient(inner, 100);
        await Assert.ThrowsAsync<IOException>(() => client.GetResponseAsync([], new() { MaxOutputTokens = 20 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Late_cancelled_response_cannot_refund_or_deliver()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = Client((_, _) => { cancellation.Cancel(); return Task.FromResult(Response(0)); });
        var client = new AiRunOutputLimitClient(inner, 100);
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync([], cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
    }

    [TestMethod]
    public async Task Concurrent_dispatch_rejects_without_charging_another_request()
    {
        var pending = new TaskCompletionSource<ChatResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var limits = new List<int?>();
        var inner = Client((options, _) => { limits.Add(options!.MaxOutputTokens); return ++calls == 1 ? pending.Task : Task.FromResult(Response(1)); });
        var client = new AiRunOutputLimitClient(inner, 100);
        var first = client.GetResponseAsync([]);
        var rejection = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
        pending.SetResult(Response(20));
        await first;
        Assert.IsTrue(((AiRunOutputBudgetException)rejection).DispatchStarted);
        await client.GetResponseAsync([]);
        CollectionAssert.AreEqual(new int?[] { 100, 80 }, limits);
    }

    [TestMethod]
    public async Task Stream_reserves_upper_bound_even_when_usage_is_zero()
    {
        var inner = Client((options, _) => { Assert.AreEqual(60, options!.MaxOutputTokens); return Task.FromResult(Response(1)); });
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Updates());
        var client = new AiRunOutputLimitClient(inner, 100);
        await foreach (var _ in client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 40 })) { }
        await client.GetResponseAsync([]);
    }

    [TestMethod]
    public async Task Stream_cannot_be_enumerated_twice()
    {
        var enumerations = 0;
        var inner = Substitute.For<IChatClient>();
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Updates(() => enumerations++));
        var client = new AiRunOutputLimitClient(inner, 100);
        var stream = client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 40 });
        await foreach (var _ in stream) { }
        var rejection = await Assert.ThrowsAsync<InvalidOperationException>(async () => { await foreach (var _ in stream) { } });
        Assert.IsTrue(((AiRunOutputBudgetException)rejection).DispatchStarted);
        Assert.AreEqual(1, enumerations);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Ignored_stream_cancellation_never_delivers_late_update(bool enumerationToken)
    {
        using var cancellation = new CancellationTokenSource();
        var inner = Substitute.For<IChatClient>();
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Updates(() => cancellation.Cancel()));
        var client = new AiRunOutputLimitClient(inner, 100);
        var stream = client.GetStreamingResponseAsync([], cancellationToken: enumerationToken ? default : cancellation.Token);
        var delivered = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in stream.WithCancellation(enumerationToken ? cancellation.Token : default)) delivered++;
        });
        Assert.AreEqual(0, delivered);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([]));
    }

    [TestMethod]
    public async Task Disposing_partial_stream_preserves_upper_bound_and_releases_dispatch_gate()
    {
        var inner = Client((options, _) => { Assert.AreEqual(60, options!.MaxOutputTokens); return Task.FromResult(Response(1)); });
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(Updates());
        var client = new AiRunOutputLimitClient(inner, 100);
        await foreach (var _ in client.GetStreamingResponseAsync([], new() { MaxOutputTokens = 40 })) break;
        await client.GetResponseAsync([]);
    }

    private static IChatClient Client(Func<ChatOptions?, CancellationToken, Task<ChatResponse>> respond)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => respond(call.ArgAt<ChatOptions?>(1), call.ArgAt<CancellationToken>(2)));
        return inner;
    }

    private static ChatResponse Response(long? output) => new(new ChatMessage(ChatRole.Assistant, "ok"))
        { Usage = new UsageDetails { InputTokenCount = 1, OutputTokenCount = output } };

    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(Action? before = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        before?.Invoke();
        yield return new(ChatRole.Assistant, "late");
        yield return new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 1, OutputTokenCount = 0 })] };
    }
}
