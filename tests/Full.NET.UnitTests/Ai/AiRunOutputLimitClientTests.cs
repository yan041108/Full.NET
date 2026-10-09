using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>请求级预算外观只降低上限，不修改原选项或暴露底层服务。</summary>
[TestClass]
public sealed class AiRunOutputLimitClientTests
{
    [TestMethod]
    [DataRow(100L, null, 100)] [DataRow(100L, 20, 20)] [DataRow(100L, 200, 100)]
    [DataRow(10000L, null, 4096)] [DataRow(long.MaxValue, 9000, 4096)]
    public async Task Buffered_dispatch_clamps_budget_without_mutating_caller(long budget, int? requested, int expected)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        using var client = new AiRunOutputLimitClient(inner, budget);
        var options = new ChatOptions { MaxOutputTokens = requested };
        await client.GetResponseAsync([new(ChatRole.User, "hello")], options);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Is<ChatOptions?>(value => value != options && value != null && value.MaxOutputTokens == expected), Arg.Any<CancellationToken>());
        Assert.AreEqual(requested, options.MaxOutputTokens);
    }

    [TestMethod]
    public void Streaming_dispatch_clamps_and_forwards_cancellation()
    {
        var inner = Substitute.For<IChatClient>();
        using var client = new AiRunOutputLimitClient(inner, 73);
        using var cancellation = new CancellationTokenSource();
        _ = client.GetStreamingResponseAsync([new(ChatRole.User, "hello")], cancellationToken: cancellation.Token);
        _ = inner.Received(1).GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Is<ChatOptions?>(value => value != null && value.MaxOutputTokens == 73), cancellation.Token);
    }

    [TestMethod]
    [DataRow(0L)] [DataRow(-1L)]
    public void Nonpositive_budget_is_rejected(long budget) => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AiRunOutputLimitClient(Substitute.For<IChatClient>(), budget));

    [TestMethod]
    [DataRow(0)] [DataRow(-1)]
    public void Nonpositive_request_is_rejected_before_dispatch(int requested)
    {
        var inner = Substitute.For<IChatClient>();
        using var client = new AiRunOutputLimitClient(inner, 100);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => client.GetResponseAsync([], new() { MaxOutputTokens = requested }));
        inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Underlying_client_and_services_cannot_be_unwrapped()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetService(Arg.Any<Type>(), Arg.Any<object?>()).Returns(inner);
        using var client = new AiRunOutputLimitClient(inner, 100);
        Assert.AreSame(client, ((IChatClient)client).GetService(typeof(IChatClient)));
        Assert.IsNull(((IChatClient)client).GetService(inner.GetType()));
        Assert.IsNull(((IChatClient)client).GetService(typeof(IChatClient), "key"));
        inner.DidNotReceive().GetService(Arg.Any<Type>(), Arg.Any<object?>());
    }
    [TestMethod]
    public void Disposing_budget_facade_preserves_coordinator_owned_client()
    {
        var inner = Substitute.For<IChatClient>();
        var client = new AiRunOutputLimitClient(inner, 100);
        client.Dispose();
        inner.DidNotReceive().Dispose();
    }

}
