using Full.NET.Modules.Ai.Runtime;
using System.Text.Json;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>输入准入在模型派发前失败，流式与缓冲调用共用同一边界。</summary>
[TestClass]
public sealed class AiRunInputAdmissionTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Oversized_text_is_rejected_without_dispatch(bool streaming)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        using var client = new AiRunOutputLimitClient(inner, 100);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => Dispatch(client,
            [new(ChatRole.User, new string('x', 262145))], null, streaming));
        Assert.IsFalse(error.DispatchStarted);
        Assert.AreEqual("ai.budget.limit_exceeded", error.Code);
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        _ = inner.DidNotReceive().GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("messages", false)] [DataRow("messages", true)]
    [DataRow("contents", false)] [DataRow("contents", true)]
    [DataRow("cumulative", false)] [DataRow("cumulative", true)]
    [DataRow("instructions", false)] [DataRow("instructions", true)]
    [DataRow("tools", false)] [DataRow("tools", true)]
    [DataRow("schema", false)] [DataRow("schema", true)]
    [DataRow("arguments", false)] [DataRow("arguments", true)]
    [DataRow("argument-text", false)] [DataRow("argument-text", true)]
    [DataRow("result", false)] [DataRow("result", true)]
    [DataRow("depth", false)] [DataRow("depth", true)]
    [DataRow("nodes", false)] [DataRow("nodes", true)]
    [DataRow("unknown", false)] [DataRow("unknown", true)]
    [DataRow("extensions", false)] [DataRow("extensions", true)]
    [DataRow("nonfinite", false)] [DataRow("nonfinite", true)]
    public async Task Every_payload_boundary_rejects_before_model(string boundary, bool streaming)
    {
        var inner = SuccessfulClient();
        var (messages, options) = Oversized(boundary);
        using var client = new AiRunOutputLimitClient(inner, 100);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => Dispatch(client, messages, options, streaming));
        Assert.IsFalse(error.DispatchStarted);
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        _ = inner.DidNotReceive().GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
        // 本地拒绝不消耗额度或留下活动调用；修改输入后可以重新派发。
        await client.GetResponseAsync([new(ChatRole.User, "ok")]);
    }

    [TestMethod]
    [DataRow("characters")] [DataRow("messages")] [DataRow("contents")]
    [DataRow("tools")] [DataRow("arguments")] [DataRow("depth")] [DataRow("nodes")]
    public async Task Inclusive_limits_accept_supported_payload(string boundary)
    {
        IEnumerable<ChatMessage> messages = [new(ChatRole.User, "ok")];
        ChatOptions? options = null;
        switch (boundary)
        {
            case "characters": messages = [new(ChatRole.User, new string('x', 262144 - ChatRole.User.Value.Length))]; break;
            case "messages": messages = Enumerable.Range(0, 128).Select(_ => new ChatMessage(ChatRole.User, "")); break;
            case "contents": messages = [new(ChatRole.User, Enumerable.Range(0, 512).Select(_ => (AIContent)new TextContent("")).ToList())]; break;
            case "tools": options = new() { Tools = Enumerable.Range(0, 64).Select(i => Declaration("tool" + i, "{}" )).ToList() }; break;
            case "arguments": messages = [Call(128)]; break;
            case "depth": messages = [Result(Json(new string('[', 31) + "0" + new string(']', 31)))]; break;
            case "nodes": messages = [Result(Json("[" + string.Join(',', Enumerable.Repeat("0", 4095)) + "]"))]; break;
        }
        var inner = SuccessfulClient();
        using var client = new AiRunOutputLimitClient(inner, 100);
        await client.GetResponseAsync(messages, options);
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Infinite_message_sequence_is_stopped_and_disposed_at_first_overflow()
    {
        var visited = 0; var disposed = false;
        IEnumerable<ChatMessage> Messages()
        {
            try { while (true) { visited++; yield return new(ChatRole.User, ""); } }
            finally { disposed = true; }
        }
        using var client = new AiRunOutputLimitClient(SuccessfulClient(), 100);
        await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => client.GetResponseAsync(Messages()));
        Assert.AreEqual(129, visited); Assert.IsTrue(disposed);
    }

    [TestMethod]
    public async Task Cancellation_during_enumeration_prevents_dispatch_and_disposes_source()
    {
        var disposed = false;
        using var cancellation = new CancellationTokenSource();
        IEnumerable<ChatMessage> Messages()
        {
            try { cancellation.Cancel(); yield return new(ChatRole.User, "ok"); }
            finally { disposed = true; }
        }
        var inner = SuccessfulClient();
        using var client = new AiRunOutputLimitClient(inner, 100);
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync(Messages(), cancellationToken: cancellation.Token));
        Assert.IsTrue(disposed);
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Request_contents_and_options_are_frozen_before_forwarding(bool streaming)
    {
        var text = new TextContent("original");
        var arguments = new Dictionary<string, object?> { ["name"] = "original" };
        var message = new ChatMessage(ChatRole.User, [text, new FunctionCallContent("id", "tool", arguments)]);
        var messages = new List<ChatMessage> { message };
        var options = new ChatOptions { Instructions = "original", Tools = [Declaration("original", "{}")] };
        var inner = SuccessfulClient(); IEnumerable<ChatMessage>? captured = null; ChatOptions? capturedOptions = null;
        void Capture(IEnumerable<ChatMessage> values, ChatOptions? valueOptions) { captured = values; capturedOptions = valueOptions; }
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => { Capture(call.ArgAt<IEnumerable<ChatMessage>>(0), call.ArgAt<ChatOptions?>(1)); return Response(); });
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => { Capture(call.ArgAt<IEnumerable<ChatMessage>>(0), call.ArgAt<ChatOptions?>(1)); return EmptyStream(); });
        using var client = new AiRunOutputLimitClient(inner, 100);
        await Dispatch(client, messages, options, streaming);
        text.Text = "changed"; arguments["name"] = "changed"; messages.Clear(); options.Instructions = "changed"; options.Tools!.Clear();
        var frozen = captured!.Single();
        Assert.AreEqual("original", frozen.Text);
        Assert.AreEqual("original", frozen.Contents.OfType<FunctionCallContent>().Single().Arguments!["name"]);
        Assert.AreEqual("original", capturedOptions!.Instructions);
        Assert.AreEqual("original", capturedOptions.Tools!.Single().Name);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Required_tool_name_counts_toward_total_input(bool streaming)
    {
        using var client = new AiRunOutputLimitClient(SuccessfulClient(), 100);
        var error = await Assert.ThrowsAsync<AiRunOutputBudgetException>(() => Dispatch(client, [],
            new() { ToolMode = ChatToolMode.RequireSpecific(new string('x', 262145)) }, streaming));
        Assert.IsFalse(error.DispatchStarted);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void Oversized_json_rejects_without_materializing_full_raw_text(bool escaped)
    {
        var message = Result(Json("\"" + (escaped ? string.Concat(Enumerable.Repeat("\\u0061", 1048576)) : new string('x', 1048576)) + "\""));
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => AiRunInputAdmission.Freeze([message], null, default));
        // 先暖身，计量只包含本次准入，避免把已有 JsonElement 的构造分配算到生产边界。
        var start = GC.GetAllocatedBytesForCurrentThread();
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => AiRunInputAdmission.Freeze([message], null, default));
        Assert.IsLessThan(131072L, GC.GetAllocatedBytesForCurrentThread() - start);
    }

    [TestMethod]
    public void Raw_json_whitespace_counts_before_cloning()
    {
        var message = Result(Json("{" + new string(' ', 1048576) + "}"));
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => AiRunInputAdmission.Freeze([message], null, default));
    }

    [TestMethod]
    public void Utf8_json_at_inclusive_character_boundary_is_admitted()
    {
        var message = Result(Json("\"" + new string('汉', 262144 - ChatRole.Tool.Value.Length - "id".Length - 2) + "\""));
        var result = AiRunInputAdmission.Freeze([message], null, default);
        Assert.AreEqual(1, result.Messages.Count);
    }

    [TestMethod]
    public void Tool_collection_is_enumerated_only_to_first_overflow_without_copying()
    {
        var visited = 0; var disposed = false; var tool = Declaration("tool", "{}");
        IEnumerable<AITool> Infinite()
        {
            try { while (true) { visited++; yield return tool; } }
            finally { disposed = true; }
        }
        var tools = Substitute.For<IList<AITool>>();
        tools.GetEnumerator().Returns(_ => Infinite().GetEnumerator());
        tools.Count.Returns(int.MaxValue);
        tools.When(t => t.CopyTo(Arg.Any<AITool[]>(), Arg.Any<int>())).Do(_ => throw new InvalidOperationException("Unbounded copy"));
        Assert.ThrowsExactly<AiRunOutputBudgetException>(() => AiRunInputAdmission.Freeze([], new() { Tools = tools }, default));
        Assert.AreEqual(65, visited); Assert.IsTrue(disposed);
    }

    [TestMethod]
    public void Options_clone_override_is_never_invoked()
    {
        var result = AiRunInputAdmission.Freeze([], new ThrowingCloneOptions(), default);
        Assert.IsInstanceOfType<ChatOptions>(result.Options);
        Assert.AreEqual(typeof(ChatOptions), result.Options!.GetType());
    }

    [TestMethod]
    public void Transmitted_tool_name_is_captured_once()
    {
        var declaration = new ChangingNameDeclaration();
        var result = AiRunInputAdmission.Freeze([], new() { Tools = [declaration] }, default);
        Assert.AreEqual("tool", result.Options!.Tools!.Single().Name);
        Assert.AreEqual(1, declaration.Reads);
    }

    private static (IEnumerable<ChatMessage>, ChatOptions?) Oversized(string boundary) => boundary switch
    {
        "messages" => (Enumerable.Range(0, 129).Select(_ => new ChatMessage(ChatRole.User, "")), null),
        "contents" => ([new(ChatRole.User, Enumerable.Range(0, 513).Select(_ => (AIContent)new TextContent("")).ToList())], null),
        "cumulative" => ([new(ChatRole.User, new string('x', 150000)), new(ChatRole.Tool, new string('x', 150000))], null),
        "instructions" => ([], new() { Instructions = new string('x', 262145) }),
        "tools" => ([], new() { Tools = Enumerable.Range(0, 65).Select(i => Declaration("tool" + i, "{}")).ToList() }),
        "schema" => ([], new() { Tools = [Declaration("tool", "{\"description\":\"" + new string('x', 262145) + "\"}")] }),
        "arguments" => ([Call(129)], null),
        "argument-text" => ([new(ChatRole.Assistant, [new FunctionCallContent("id", "tool", new Dictionary<string, object?> { ["text"] = new string('x', 262145) })])], null),
        "result" => ([Result(new string('x', 262145))], null),
        "depth" => ([Result(Json(new string('[', 32) + "0" + new string(']', 32)))], null),
        "nodes" => ([Result(Json("[" + string.Join(',', Enumerable.Repeat("0", 4096)) + "]"))], null),
        "unknown" => ([Result(new UnknownValue())], null),
        "extensions" => ([new ChatMessage(ChatRole.User, "ok") { AdditionalProperties = new() { ["hidden"] = "unbounded" } }], null),
        "nonfinite" => ([Result(double.NaN)], null),
        _ => throw new ArgumentOutOfRangeException(nameof(boundary))
    };

    private static ChatMessage Call(int count) => new(ChatRole.Assistant,
        [new FunctionCallContent("id", "tool", Enumerable.Range(0, count).ToDictionary(i => "key" + i, _ => (object?)"value"))]);
    private static ChatMessage Result(object value) => new(ChatRole.Tool, [new FunctionResultContent("id", value)]);
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static AITool Declaration(string name, string schema) => AIFunctionFactory.CreateDeclaration(name, "description", Json(schema));
    private static ChatResponse Response() => new(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = new() { InputTokenCount = 0, OutputTokenCount = 0 } };
    private static IChatClient SuccessfulClient()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(_ => Response());
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyStream());
        return inner;
    }
    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyStream() { await Task.Yield(); yield break; }
    private sealed class UnknownValue { public override string ToString() => throw new InvalidOperationException("Unknown objects must not be inspected."); }
    private sealed class ThrowingCloneOptions : ChatOptions { public override ChatOptions Clone() => throw new InvalidOperationException("Clone must not copy unchecked collections."); }
    private sealed class ChangingNameDeclaration : AIFunctionDeclaration
    {
        public int Reads { get; private set; }
        public override string Name => ++Reads == 1 ? "tool" : new string('x', 262145);
        public override string Description => "description";
        public override JsonElement JsonSchema => Json("{}");
    }

    private static async Task Dispatch(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions? options, bool streaming)
    {
        if (streaming) { await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { } }
        else await client.GetResponseAsync(messages, options);
    }
}
