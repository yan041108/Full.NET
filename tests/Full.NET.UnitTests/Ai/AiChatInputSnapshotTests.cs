using System.Collections;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.AI;

namespace Full.NET.UnitTests.Ai;

/// <summary>预算和派发必须读取有界的相同正文；外部集合不能在工厂等待期间改写已批准请求。</summary>
[TestClass]
public sealed class AiChatInputSnapshotTests
{
    [TestMethod]
    [DataRow(false, "count")][DataRow(false, "negative")][DataRow(false, "count-fault")][DataRow(false, "index-fault")]
    [DataRow(false, "role-null")][DataRow(false, "role-unknown")][DataRow(false, "content-null")]
    [DataRow(false, "characters")][DataRow(false, "unicode-high")][DataRow(false, "unicode-low")]
    [DataRow(true, "count")][DataRow(true, "negative")][DataRow(true, "count-fault")][DataRow(true, "index-fault")]
    [DataRow(true, "role-null")][DataRow(true, "role-unknown")][DataRow(true, "content-null")]
    [DataRow(true, "characters")][DataRow(true, "unicode-high")][DataRow(true, "unicode-low")]
    public async Task Chat_snapshot_invalid_input_fails_before_factory_or_unbounded_enumeration(bool streaming, string fault)
    {
        var row = fault switch
        {
            "role-null" => (null!, "body"), "role-unknown" => ("private-role", "body"), "content-null" => ("user", null!),
            "characters" => ("user", new string('x', 64001)), "unicode-high" => ("user", "\ud800"),
            "unicode-low" => ("user", "\udc00"), _ => ("user", "body")
        };
        var input = new ControlledHistory([row]) { DeclaredCount = fault == "count" ? 41 : fault == "negative" ? -1 : 1,
            CountFailure = fault == "count-fault", IndexFailure = fault == "index-fault" };
        var factory = new Factory(); using var bindings = new AiModelBindingScope();
        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
        {
            if (streaming) await new AiChatCompletionStreamer([factory], bindings).StreamAsync(PaidChatFactory.Model(), input, _ => Task.CompletedTask);
            else _ = AiChatBudgetRequest.Create(Guid.CreateVersion7(), PaidChatFactory.Model(), input);
        });
        Assert.DoesNotContain("private", exception.Message, StringComparison.Ordinal); Assert.IsNull(exception.InnerException);
        Assert.AreEqual(0, factory.Creates); Assert.AreEqual(0, input.Enumerations); Assert.AreEqual(1, input.CountReads);
        Assert.AreEqual(fault is "count" or "negative" or "count-fault" ? 0 : 1, input.IndexReads);
    }

    [TestMethod]
    [DataRow(false, "empty")][DataRow(false, "messages")][DataRow(false, "characters")][DataRow(false, "unicode")][DataRow(false, "roles")][DataRow(false, "whitespace")]
    [DataRow(true, "empty")][DataRow(true, "messages")][DataRow(true, "characters")][DataRow(true, "unicode")][DataRow(true, "roles")][DataRow(true, "whitespace")]
    public async Task Chat_snapshot_inclusive_limits_preserve_bytes_and_original_fingerprint(bool streaming, string boundary)
    {
        (string RoleKey, string Content)[] rows = boundary switch
        {
            "empty" => [], "messages" => Enumerable.Repeat(("user", "body"), 40).ToArray(),
            "characters" => [("assistant", new string('中', 64000))], "unicode" => [("user", "😀 中文")],
            "roles" => new[] { "system", "developer", "user", "assistant", "tool" }.Select(role => (role, "body")).ToArray(),
            _ => [("user", " \r\n\t正文  ")]
        };
        var input = new ControlledHistory(rows); var model = PaidChatFactory.Model(); var operation = Guid.CreateVersion7();
        var original = AiChatBudgetRequest.Create(operation, model, rows);
        // 基线041ef旧算法由独立长度前缀编码计算的固定值，不能以两次调用当前实现证明兼容。
        Assert.AreEqual(ExpectedHash(boundary), original.RequestHash);
        Assert.AreEqual(boundary switch { "empty" => 0L, "messages" => 2720L, "characters" => 192064L,
            "unicode" => 75L, "roles" => 340L, _ => 76L }, original.InputTokenLimit);
        var factory = new Factory(); using var bindings = new AiModelBindingScope();
        if (streaming)
        {
            Assert.AreEqual("ok", (await new AiChatCompletionStreamer([factory], bindings).StreamAsync(model, input, _ => Task.CompletedTask)).Content);
            CollectionAssert.AreEqual(rows, factory.Captured!); Assert.IsTrue(factory.Disposed);
        }
        else Assert.AreEqual(original, AiChatBudgetRequest.Create(operation, model, input));
        Assert.AreEqual(1, input.CountReads); Assert.AreEqual(rows.Length, input.IndexReads); Assert.AreEqual(0, input.Enumerations);
    }

    [TestMethod]
    [DataRow("factory", "replace")][DataRow("factory", "clear")][DataRow("factory", "append")][DataRow("factory", "role")]
    [DataRow("dispatch", "replace")][DataRow("dispatch", "clear")][DataRow("dispatch", "append")][DataRow("dispatch", "role")]
    public async Task Chat_snapshot_factory_wait_or_late_enumeration_cannot_change_reserved_input(string phase, string mutation)
    {
        var rows = new List<(string RoleKey, string Content)> { ("user", "原始正文😀") };
        var model = PaidChatFactory.Model(); var operation = Guid.CreateVersion7(); var reserved = AiChatBudgetRequest.Create(operation, model, rows);
        var factory = new Factory(); Action change = () =>
        {
            if (mutation == "clear") rows.Clear();
            else if (mutation == "append") rows.Add(("user", "迟到私有内容"));
            else rows[0] = mutation == "role" ? ("system", "迟到系统指令") : ("user", "迟到私有内容");
        };
        if (phase == "factory") factory.BeforeCreate = change; else factory.BeforeStreaming = change;
        using var bindings = new AiModelBindingScope();
        await new AiChatCompletionStreamer([factory], bindings).StreamAsync(model, rows, _ => Task.CompletedTask);
        CollectionAssert.AreEqual(new[] { ("user", "原始正文😀") }, factory.Captured!);
        Assert.AreEqual(reserved, AiChatBudgetRequest.Create(operation, model, factory.Captured!));
        Assert.IsTrue(factory.Disposed);
    }

    [TestMethod][DataRow("before", false)][DataRow("before", true)][DataRow("count", false)][DataRow("count", true)][DataRow("index", false)][DataRow("index", true)]
    public async Task Chat_snapshot_cancellation_during_copy_never_creates_client(string phase, bool invalidUnicode)
    {
        using var stop = new CancellationTokenSource();
        var input = new ControlledHistory([("user", invalidUnicode ? "\ud800" : "body")]);
        if (phase == "before") stop.Cancel(); else if (phase == "count") input.OnCount = stop.Cancel; else input.OnIndex = stop.Cancel;
        var factory = new Factory(); using var bindings = new AiModelBindingScope();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AiChatCompletionStreamer([factory], bindings)
            .StreamAsync(PaidChatFactory.Model(), input, _ => Task.CompletedTask, stop.Token));
        Assert.AreEqual(0, factory.Creates); Assert.AreEqual(0, input.Enumerations);
        Assert.AreEqual(phase == "before" ? 0 : 1, input.CountReads); Assert.AreEqual(phase == "index" ? 1 : 0, input.IndexReads);
    }

    private static string ExpectedHash(string boundary) => boundary switch
    {
        "empty" => "67F5A089F0D8D74444CC6315F413B8D5EC51CC2AD49735156555067B18C2E42B",
        "messages" => "45F108D1B8F52B500883BA4BDFD78D122605BB4F4B1F5D6E5B9EA51422B31D96",
        "characters" => "58F15696121EE68FE4C7B2055E0BDB96EC98A241B20CFA7F79CE21D70E1E4B34",
        "unicode" => "1A3ADFCCBD4A3652F021FC99AE1AA77EA0B33F1DB3C81ACD80CDD5760FD72951",
        "roles" => "7CF95CCC3282FF7D2D8802574A4C87ADA237C9A1BC83A8D38800508C8541573C",
        _ => "BDD66B1A1C8F00093C1825E696796096660FAEA5F4840D7648E91D3AF9F6052E"
    };

    /// <summary>枚举故意失败；只有一次Count与有限次索引能够获得正文。</summary>
    private sealed class ControlledHistory((string RoleKey, string Content)[] rows) : IReadOnlyList<(string RoleKey, string Content)>
    {
        internal int DeclaredCount { get; set; } = rows.Length;
        internal int CountReads, IndexReads, Enumerations;
        internal bool CountFailure, IndexFailure;
        internal Action? OnCount, OnIndex;
        public int Count { get { CountReads++; if (CountFailure) throw new InvalidOperationException("private collection failure"); OnCount?.Invoke(); return DeclaredCount; } }
        public (string RoleKey, string Content) this[int index]
        { get { IndexReads++; if (IndexFailure) throw new IOException("private source failure"); OnIndex?.Invoke(); return rows[index]; } }
        public IEnumerator<(string RoleKey, string Content)> GetEnumerator() { Enumerations++; throw new InvalidOperationException("private enumerator must not run"); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Factory : IAiModelClientFactory
    {
        public string ProviderKey => "ollama";
        internal int Creates; internal bool Disposed;
        internal Action? BeforeCreate, BeforeStreaming;
        internal (string RoleKey, string Content)[]? Captured;
        public async ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token = default)
        { Creates++; BeforeCreate?.Invoke(); await Task.Yield(); return new Client(this); }
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token = default) => throw new AssertFailedException();
        private sealed class Client(Factory owner) : IChatClient
        {
            public object? GetService(Type type, object? key = null) => null;
            public void Dispose() => owner.Disposed = true;
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default) => throw new AssertFailedException();
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default)
            {
                owner.BeforeStreaming?.Invoke(); owner.Captured = messages.Select(m => (m.Role.Value, m.Text ?? string.Empty)).ToArray(); return Updates();
            }
            private static async IAsyncEnumerable<ChatResponseUpdate> Updates()
            {
                await Task.CompletedTask;
                yield return new() { Contents = [new TextContent("ok"), new UsageContent(new() { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = 10 })] };
            }
        }
    }
}

/// <summary>真实聊天编排遇到不合规历史时不能预留预算或派发，仍清理自己已取得的代次。</summary>
public sealed partial class AiChatPaidBoundaryTests
{
    [TestMethod][DataRow(false, "history-many")][DataRow(false, "history-unicode")][DataRow(false, "history-role")]
    [DataRow(true, "history-many")][DataRow(true, "history-unicode")][DataRow(true, "history-role")]
    public async Task Chat_snapshot_invalid_history_has_no_reservation_or_dispatch(bool scoped, string fault)
        => await RunPaidBoundaryAsync(scoped, fault);
}
