using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>整轮工具请求先冻结和校验，再执行任何工具；未知对象不允许调用 ToString 降级。</summary>
[TestClass]
public sealed class AiAgentToolCallAdmissionTests
{
    [TestMethod]
    [DataRow("object")] [DataRow("date")] [DataRow("guid")] [DataRow("decimal")]
    [DataRow("float")] [DataRow("nan")] [DataRow("infinity")] [DataRow("bytes")]
    [DataRow("dictionary")] [DataRow("undefined")] [DataRow("duplicate-json")]
    [DataRow("deep-json")] [DataRow("large-text")] [DataRow("large-json")]
    [DataRow("many-arguments")] [DataRow("duplicate-id")] [DataRow("duplicate-other-tool")]
    [DataRow("empty-id")] [DataRow("large-id")] [DataRow("control-id")]
    [DataRow("empty-name")] [DataRow("large-name")] [DataRow("informational")]
    [DataRow("extension")] [DataRow("raw")] [DataRow("control-name")] [DataRow("exception")]
    public async Task Invalid_later_call_rejects_whole_batch_before_any_side_effect(string fault)
    {
        var unknown = new UnknownValue();
        var calls = new[] { Call("first"), Call("second") };
        var arguments = calls[1].Arguments!;
        switch (fault)
        {
            case "object": arguments["value"] = unknown; break;
            case "date": arguments["value"] = DateTimeOffset.UnixEpoch; break;
            case "guid": arguments["value"] = Guid.Empty; break;
            case "decimal": arguments["value"] = 1m; break;
            case "float": arguments["value"] = 1f; break;
            case "nan": arguments["value"] = double.NaN; break;
            case "infinity": arguments["value"] = double.PositiveInfinity; break;
            case "bytes": arguments["value"] = new byte[] { 1 }; break;
            case "dictionary": arguments["value"] = new Dictionary<string, object?>(); break;
            case "undefined": arguments["value"] = default(JsonElement); break;
            case "duplicate-json": arguments["value"] = Json("{\"x\":1,\"x\":2}"); break;
            case "deep-json": arguments["value"] = Json(new string('[', 33) + "0" + new string(']', 33)); break;
            case "large-text": arguments["value"] = new string('x', 262145); break;
            case "large-json": arguments["value"] = Json("{\"x\":\"" + new string('x', 262145) + "\"}"); break;
            case "many-arguments": foreach (var i in Enumerable.Range(0, 129)) arguments[$"a{i}"] = i; break;
            case "duplicate-id": calls[1] = Call("first"); break;
            case "duplicate-other-tool": calls[1] = new("first", "ai.models.list", new Dictionary<string, object?>()); break;
            case "empty-id": calls[1] = Call(""); break;
            case "large-id": calls[1] = Call(new string('x', 257)); break;
            case "control-id": calls[1] = Call("second\0hidden"); break;
            case "empty-name": calls[1] = new("second", "", new Dictionary<string, object?>()); break;
            case "large-name": calls[1] = new("second", new string('x', 257), new Dictionary<string, object?>()); break;
            case "control-name": calls[1] = new("second", "ai.tools.ping\0hidden", new Dictionary<string, object?>()); break;
            case "informational": calls[1].InformationalOnly = true; break;
            case "extension": calls[1].AdditionalProperties = new() { ["hidden"] = "value" }; break;
            case "raw": calls[1].RawRepresentation = unknown; break;
            case "exception": calls[1] = new("second", "ai.tools.ping", null) { Exception = new JsonException("invalid provider JSON") }; break;
        }
        var fixture = Fixture(Response(calls));
        Exception? error = null;
        try { await new AgentToolLoop().RunAsync(fixture.Request); }
        catch (Exception exception) { error = exception; }
        Assert.AreEqual(0, fixture.Resolved.Count, "整批校验失败前不能分配派发身份。");
        await fixture.Executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
        Assert.IsInstanceOfType<InvalidOperationException>(error);
        Assert.AreEqual(0, unknown.ToStringCalls);
    }

    [TestMethod]
    [DataRow("null")] [DataRow("bool")] [DataRow("int")] [DataRow("long")]
    [DataRow("double")] [DataRow("string")] [DataRow("json")]
    public async Task Supported_argument_is_lossless_and_dispatches_once(string kind)
    {
        object? value = kind switch
        {
            "null" => null, "bool" => true, "int" => 42, "long" => long.MaxValue,
            "double" => 1.25d, "string" => "中文", _ => Json("{\"a\":[1,null,true]}")
        };
        var call = Call("valid"); call.Arguments!["value"] = value;
        var fixture = Fixture(Response([call]));
        var result = await new AgentToolLoop().RunAsync(fixture.Request);
        Assert.AreEqual("done", result.Text);
        var invocation = (ToolInvocation)fixture.Executor.ReceivedCalls().Single().GetArguments()[0]!;
        var actual = invocation.Arguments.GetProperty("value");
        switch (kind)
        {
            case "null": Assert.AreEqual(JsonValueKind.Null, actual.ValueKind); break;
            case "bool": Assert.IsTrue(actual.GetBoolean()); break;
            case "int": Assert.AreEqual(42, actual.GetInt32()); break;
            case "long": Assert.AreEqual(long.MaxValue, actual.GetInt64()); break;
            case "double": Assert.AreEqual(1.25d, actual.GetDouble()); break;
            case "string": Assert.AreEqual("中文", actual.GetString()); break;
            default: Assert.AreEqual(3, actual.GetProperty("a").GetArrayLength()); break;
        }
    }

    [TestMethod]
    public async Task Later_arguments_are_frozen_before_first_tool_await()
    {
        var calls = new[] { Call("first"), Call("second") };
        calls[1].Arguments!["value"] = "original";
        var fixture = Fixture(Response(calls));
        fixture.Executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            calls[1].Arguments!["value"] = "changed";
            var invocation = (ToolInvocation)call[0]!;
            if (invocation.Arguments.TryGetProperty("value", out var value)) Assert.AreEqual("original", value.GetString());
            return Success();
        });
        await new AgentToolLoop().RunAsync(fixture.Request);
        Assert.AreEqual(2, fixture.Resolved.Count);
    }

    [TestMethod]
    public async Task Later_json_is_owned_before_first_tool_disposes_source()
    {
        using var document = JsonDocument.Parse("{\"value\":42}");
        var calls = new[] { Call("first"), Call("second") };
        calls[1].Arguments!["value"] = document.RootElement;
        var fixture = Fixture(Response(calls));
        fixture.Executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            document.Dispose();
            var invocation = (ToolInvocation)call[0]!;
            if (invocation.Arguments.TryGetProperty("value", out var value)) Assert.AreEqual(42, value.GetProperty("value").GetInt32());
            return Success();
        });
        Assert.AreEqual("done", (await new AgentToolLoop().RunAsync(fixture.Request)).Text);
    }

    [TestMethod]
    public async Task Duplicate_call_id_in_later_model_round_never_reuses_operation()
    {
        var fixture = Fixture(Response([Call("same")]), Response([Call("same")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(1, fixture.Resolved.Count);
        await fixture.Executor.Received(1).ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cumulative_batch_payload_is_bounded_before_first_tool()
    {
        var calls = new[] { Call("first"), Call("second") };
        foreach (var call in calls) call.Arguments!["value"] = new string('x', 140000);
        var fixture = Fixture(Response(calls));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(0, fixture.Resolved.Count);
    }

    [TestMethod]
    public async Task Null_argument_dictionary_is_empty_object()
    {
        var fixture = Fixture(Response([new("empty", "ai.tools.ping", null)]));
        await new AgentToolLoop().RunAsync(fixture.Request);
        var invocation = (ToolInvocation)fixture.Executor.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.AreEqual("{}", invocation.Arguments.GetRawText());
    }

    [TestMethod]
    public async Task Many_json_nodes_are_rejected_before_any_tool()
    {
        var call = Call("nodes"); call.Arguments!["value"] = Json("[" + string.Join(',', Enumerable.Repeat("0", 4097)) + "]");
        var fixture = Fixture(Response([call]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(0, fixture.Resolved.Count);
    }

    [TestMethod]
    public async Task Primitive_argument_nodes_share_same_total_budget_as_json_elements()
    {
        var calls = Enumerable.Range(0, 33).Select(i => Call($"nodes-{i}")).ToArray();
        foreach (var call in calls)
            foreach (var i in Enumerable.Range(0, 128)) call.Arguments![$"a{i}"] = 0;
        var fixture = Fixture(Response(calls));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(0, fixture.Resolved.Count);
    }

    [TestMethod]
    [DataRow("system")] [DataRow("user")] [DataRow("tool")]
    public async Task Provider_cannot_promote_response_into_other_history_role(string role)
    {
        var response = Response([Call("first")]);
        response.Messages.Add(new(role switch { "system" => ChatRole.System, "user" => ChatRole.User, _ => ChatRole.Tool }, [Call("second")]));
        var fixture = Fixture(response);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentToolLoop().RunAsync(fixture.Request));
        Assert.AreEqual(0, fixture.Resolved.Count);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Response_diagnostic_metadata_is_discarded_without_conversion_or_forwarding(bool raw)
    {
        var unknown = new UnknownValue(); var response = Response([Call("valid")]);
        if (raw) response.RawRepresentation = unknown;
        else response.AdditionalProperties = new() { ["diagnostic"] = unknown };
        var fixture = Fixture(response); var index = 0;
        fixture.Client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            foreach (var message in (IEnumerable<ChatMessage>)call[0]!)
            {
                Assert.IsNull(message.RawRepresentation); Assert.IsNull(message.AdditionalProperties);
                foreach (var content in message.Contents) { Assert.IsNull(content.RawRepresentation); Assert.IsNull(content.AdditionalProperties); }
            }
            return ++index == 1 ? response : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        });
        Assert.AreEqual("done", (await new AgentToolLoop().RunAsync(fixture.Request)).Text);
        Assert.AreEqual(0, unknown.ToStringCalls);
        Assert.AreEqual(1, fixture.Resolved.Count);
    }

    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static FunctionCallContent Call(string id) => new(id, "ai.tools.ping", new Dictionary<string, object?>());
    private static ChatResponse Response(FunctionCallContent[] calls) => new(new ChatMessage(ChatRole.Assistant, calls.Cast<AIContent>().ToList()));
    private static ToolExecutionResult Success() => new("succeeded", Json("true"), null);
    private static (AgentToolLoopRequest Request, IAgentToolExecutor Executor, List<string> Resolved, IChatClient Client) Fixture(params ChatResponse[] responses)
    {
        var client = Substitute.For<IChatClient>(); var index = 0;
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => index < responses.Length ? responses[index++] : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        var executor = Substitute.For<IAgentToolExecutor>();
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(), Arg.Any<CancellationToken>()).Returns(Success());
        var resolved = new List<string>();
        return (new(Guid.CreateVersion7(), AgentDefinitionRegistry.ReadOnlyToolLoopKey, 1, "prompt", client, executor,
            new AgentToolRegistry([new("ai.tools.ping", 1, "permission", "none", true, null), new("ai.models.list", 1, "permission", "none", true, null)]),
            id => { resolved.Add(id); return Guid.CreateVersion7(); }), executor, resolved, client);
    }
    private sealed class UnknownValue
    {
        internal int ToStringCalls { get; private set; }
        public override string ToString() { ToStringCalls++; return "unknown-sensitive-value"; }
    }
}
