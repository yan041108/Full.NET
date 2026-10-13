using System.Runtime.CompilerServices;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.AI;

namespace Full.NET.UnitTests.Ai;

/// <summary>受控中立客户端只注入实际派发/回执/释放时序，不替换聊天编排。</summary>
internal sealed class PaidChatFactory(string fault, CancellationTokenSource? cancellation = null) : IAiModelClientFactory
{
    public string ProviderKey => "ollama";
    internal readonly IOException Marker = new("供应商原始信息不得进入用户错误");
    internal UsageDetails? Usage = new() { InputTokenCount = 7, OutputTokenCount = 3, CachedInputTokenCount = 2, ReasoningTokenCount = 1, TotalTokenCount = 10 };
    internal IAsyncEnumerable<ChatResponseUpdate>? StreamOverride;
    internal int Creates, Calls; internal bool Disposed; internal ChatOptions? Options;
    public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token = default)
    {
        Creates++; if (fault == "factory") throw Marker;
        if (fault == "factory_cancel") cancellation!.Cancel();
        return ValueTask.FromResult<IChatClient>(fault == "null_factory" ? null! : new Client(this, fault, cancellation));
    }
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token = default) => throw new AssertFailedException();
    private sealed class Client(PaidChatFactory f, string fault, CancellationTokenSource? cancellation) : IChatClient
    {
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { f.Disposed = true; if (fault == "disposed") throw f.Marker; if (fault == "disposed_cancel") cancellation!.Cancel(); }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default) => throw new AssertFailedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken token = default)
        { f.Calls++; f.Options = options; return f.StreamOverride ?? (fault == "null_source" ? null! : fault == "current_cancel" ? new PaidCancellingUpdates(cancellation!) : Updates(f, fault, token)); }
    }
    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(PaidChatFactory f, string fault, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.CompletedTask;
        if (fault == "provider") throw f.Marker;
        yield return new() { Contents = fault == "unknown" ? [new TextContent("正文")]
            : [new TextContent("正文"), new UsageContent(f.Usage!)], FinishReason = ChatFinishReason.Stop };
    }
    internal static AiModelConfigRecord Model() => new() { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true, ProviderKey = "ollama", ModelId = "model", EndpointBaseUrl = "https://provider.test" };
}

/// <summary>普通流回执必须在同帧正文之前校验，取消不能派发或交付迟到正文。</summary>
[TestClass]
public sealed class AiChatCompletionReceiptTests
{
    [TestMethod] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)] [DataRow(10)] [DataRow(11)] [DataRow(12)] [DataRow(13)]
    public async Task Paid_receipt_chat_stream_rejects_invalid_metadata_before_delta(int fault)
    {
        var f = new PaidChatFactory("success") { Usage = PaidReceiptCases.Usage(fault) };
        using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope(); var streamer = new AiChatCompletionStreamer([f], bindings); var deltas = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => streamer.StreamAsync(PaidChatFactory.Model(), [("user", "问题")], _ => { deltas++; return Task.CompletedTask; }));
        Assert.AreEqual(0, deltas); Assert.IsTrue(f.Disposed);
    }
    [TestMethod]
    public async Task Paid_receipt_chat_factory_cancellation_prevents_stream_call()
    {
        using var token = new CancellationTokenSource(); var f = new PaidChatFactory("factory_cancel", token);
        using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope(); var streamer = new AiChatCompletionStreamer([f], bindings); var deltas = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => streamer.StreamAsync(PaidChatFactory.Model(), [], _ => { deltas++; return Task.CompletedTask; }, token.Token));
        Assert.AreEqual(0, f.Calls); Assert.AreEqual(0, deltas); Assert.IsTrue(f.Disposed);
    }
    [TestMethod] [DataRow("null_factory")] [DataRow("null_source")]
    public async Task Paid_receipt_chat_missing_provider_result_is_explicit_failure(string fault)
    {
        var f = new PaidChatFactory(fault); using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new AiChatCompletionStreamer([f], bindings).StreamAsync(PaidChatFactory.Model(), [], _ => Task.CompletedTask));
    }
    [TestMethod]
    public async Task Paid_receipt_chat_current_cancellation_has_no_late_delta()
    {
        using var token = new CancellationTokenSource(); var f = new PaidChatFactory("current_cancel", token);
        using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope(); var deltas = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => new AiChatCompletionStreamer([f], bindings).StreamAsync(PaidChatFactory.Model(), [], _ => { deltas++; return Task.CompletedTask; }, token.Token));
        Assert.AreEqual(0, deltas); Assert.IsTrue(f.Disposed);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Paid_receipt_chat_eof_survives_enumerator_cleanup(bool cancel)
    {
        using var token = new CancellationTokenSource();
        var f = new PaidChatFactory("success") { StreamOverride = new PaidEofCleanupUpdates(cancel, token) };
        using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope();
        var receipt = new AiChatStreamingReceipt();
        await Assert.ThrowsAsync<Exception>(() => new AiChatCompletionStreamer([f], bindings).StreamAsync(
            PaidChatFactory.Model(), [], _ => Task.CompletedTask, token.Token, receipt: receipt));
        Assert.AreEqual(new AiOperationUsage(7, 3, 2), receipt.Freeze()); Assert.IsTrue(f.Disposed);
    }
    [TestMethod]
    public async Task Paid_receipt_chat_sends_declared_output_limit()
    {
        var f = new PaidChatFactory("success"); using var bindings = new Full.NET.Modules.Ai.Security.AiModelBindingScope();
        await new AiChatCompletionStreamer([f], bindings).StreamAsync(PaidChatFactory.Model(), [], _ => Task.CompletedTask);
        Assert.AreEqual(AiChatContentPolicy.MaxCompletionTokens, f.Options?.MaxOutputTokens);
    }
}

/// <summary>模拟提供者在 Current 读取中触发取消；返回值不能越过取消成为迟到正文。</summary>
internal sealed class PaidCancellingUpdates(CancellationTokenSource token) : IAsyncEnumerable<ChatResponseUpdate>, IAsyncEnumerator<ChatResponseUpdate>
{
    private bool moved;
    public ChatResponseUpdate Current { get { token.Cancel(); return new() { Contents = [new TextContent("迟到正文"), new UsageContent(new() { InputTokenCount = 1, OutputTokenCount = 0 })] }; } }
    public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
    public ValueTask<bool> MoveNextAsync() { var value = !moved; moved = true; return ValueTask.FromResult(value); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>明确EOF之后的清理故障不能抹除已经完整消费的供应商计量。</summary>
internal sealed class PaidEofCleanupUpdates(bool cancel, CancellationTokenSource token) : IAsyncEnumerable<ChatResponseUpdate>, IAsyncEnumerator<ChatResponseUpdate>
{
    private bool moved;
    public ChatResponseUpdate Current => new() { Contents = [new UsageContent(new() { InputTokenCount = 7, OutputTokenCount = 3, CachedInputTokenCount = 2, TotalTokenCount = 10 })] };
    public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
    public ValueTask<bool> MoveNextAsync() { var value = !moved; moved = true; return ValueTask.FromResult(value); }
    public ValueTask DisposeAsync()
    { if (cancel) { token.Cancel(); return ValueTask.FromException(new OperationCanceledException(token.Token)); } return ValueTask.FromException(new IOException("controlled EOF cleanup failure")); }
}
