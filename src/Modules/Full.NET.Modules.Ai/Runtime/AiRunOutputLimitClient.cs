using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Budgets;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>Agent 模型请求共用输入规模准入、输出额度与输入回执额度；底层客户端由协调器独占并释放。</summary>
internal sealed class AiRunOutputLimitClient : IChatClient
{
    private readonly IChatClient _innerClient;
    private long _remainingTokens;
    private long? _remainingInputTokens;
    private int _active;

    /// <summary>从已确认的历史用量扣减额度；输入按供应商回执扣减，不冒充派发前的精确分词硬限。</summary>
    internal AiRunOutputLimitClient(IChatClient innerClient, long outputTokenLimit, long? consumedOutputTokens = 0,
        long? inputTokenLimit = null, long? consumedInputTokens = 0)
    {
        if (outputTokenLimit <= 0) throw new ArgumentOutOfRangeException(nameof(outputTokenLimit));
        if (consumedOutputTokens is < 0 || consumedOutputTokens > outputTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(consumedOutputTokens));
        if (inputTokenLimit is <= 0 or > Full.NET.AI.Abstractions.Budgets.AiExecutionBudget.MaximumTokens)
            throw new ArgumentOutOfRangeException(nameof(inputTokenLimit));
        if (consumedInputTokens is < 0 || consumedInputTokens > inputTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(consumedInputTokens));
        ArgumentNullException.ThrowIfNull(innerClient);
        _innerClient = innerClient;
        _remainingTokens = outputTokenLimit - (consumedOutputTokens ?? outputTokenLimit);
        _remainingInputTokens = inputTokenLimit - (consumedInputTokens ?? inputTokenLimit);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 底层提供者可能忽略令牌；已取消请求不得进入真实客户端调用。
        cancellationToken.ThrowIfCancellationRequested();
        var admitted = AiRunInputAdmission.Freeze(messages, options, cancellationToken);
        var limited = Reserve(admitted.Options);
        return GetReservedResponseAsync(admitted.Messages, limited, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var admitted = AiRunInputAdmission.Freeze(messages, options, cancellationToken);
        var limited = Reserve(admitted.Options);
        // 流可能只有部分用量或被提前释放，保守占用全部剩余输入，不据此为后续请求退回额度。
        var reservedInputTokens = _remainingInputTokens;
        if (reservedInputTokens.HasValue) _remainingInputTokens = 0;
        AiChatInputBudgetScope? exactInput = null;
        try
        {
            exactInput = reservedInputTokens is { } limit ? AiChatInputBudgetScope.Capture(limit) : null;
            using var activation = exactInput?.Enter();
            var source = _innerClient.GetStreamingResponseAsync(admitted.Messages, limited, cancellationToken);
            if (source is null) throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
            return new ReservedStream(this, source, limited.MaxOutputTokens!.Value, reservedInputTokens, cancellationToken, exactInput);
        }
        catch
        {
            exactInput?.Dispose();
            _remainingTokens = 0;
            Volatile.Write(ref _active, 0);
            throw;
        }
    }

    // 框架使用传入的客户端；不泄漏底层服务，防止绕过预算外观调用真实客户端。
    object? IChatClient.GetService(Type serviceType, object? serviceKey) =>
        serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

    // 外观不拥有底层资源；即使执行器释放外观，协调器仍能按原有边界释放真实客户端。
    public void Dispose() { }

    private ChatOptions Reserve(ChatOptions? options)
    {
        if (options?.MaxOutputTokens is <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        var limited = options?.Clone() ?? new ChatOptions();
        // 一个运行只顺序派发；并发请求不能竞争、重复消费或绕过同一剩余额度。
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new AiRunOutputBudgetException(true, "ai.budget.operation_conflict");
        if (_remainingTokens <= 0 || _remainingInputTokens is <= 0)
        {
            Volatile.Write(ref _active, 0);
            throw new AiRunOutputBudgetException(false);
        }
        limited.MaxOutputTokens = (int)Math.Min(Math.Min(options?.MaxOutputTokens ?? 4096, 4096), _remainingTokens);
        _remainingTokens -= limited.MaxOutputTokens.Value;
        return limited;
    }

    private async Task<ChatResponse> GetReservedResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var exactInput = _remainingInputTokens is { } limit ? AiChatInputBudgetScope.Begin(limit) : null;
            var response = await _innerClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (response is null) throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
            AiOperationUsage usage;
            try { usage = AiModelUsagePolicy.Read(response.Usage); }
            catch (AiBudgetException) { throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage"); }
            var output = usage.OutputTokens;
            var input = usage.InputTokens;
            if (output > options.MaxOutputTokens)
                throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
            if (_remainingInputTokens is { } remainingInput)
            {
                // 回执超量时调用已经发生，拒绝交付并保留外部结果待核对；缺失输入不能授予下一次派发。
                if (input > remainingInput) throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
                _remainingInputTokens = input.HasValue ? remainingInput - input.Value : 0;
            }
            // 只有合法、完整的输出回执才能退回未用额度；未知用量保留本次全部预留。
            if (output.HasValue) _remainingTokens += options.MaxOutputTokens!.Value - output.Value;
            return response;
        }
        catch
        {
            // 异常、取消或不可信回执后不再派发；不能从未收到响应推断外部没有发生费用。
            _remainingTokens = 0;
            throw;
        }
        finally { Volatile.Write(ref _active, 0); }
    }

    /// <summary>流只允许一次枚举；保留输出上限与所有剩余输入，不凭部分流退费。</summary>
    private sealed class ReservedStream(AiRunOutputLimitClient owner, IAsyncEnumerable<ChatResponseUpdate> source,
        int reservedTokens, long? reservedInputTokens, CancellationToken dispatchToken, AiChatInputBudgetScope? exactInput) : IAsyncEnumerable<ChatResponseUpdate>
    {
        private int _enumerated;
        private int _closed;

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _enumerated, 1, 0) != 0)
                throw new AiRunOutputBudgetException(true, "ai.budget.operation_conflict");
            return new OwnedEnumerator(this, Enumerate(cancellationToken).GetAsyncEnumerator());
        }

        // 异步迭代器在首次 MoveNext 前释放不会执行方法体的 finally，外层仍须结束工厂已捕获的调用范围。
        private sealed class OwnedEnumerator(ReservedStream stream, IAsyncEnumerator<ChatResponseUpdate> inner) : IAsyncEnumerator<ChatResponseUpdate>
        {
            public ChatResponseUpdate Current => inner.Current;
            public ValueTask<bool> MoveNextAsync() => inner.MoveNextAsync();
            public async ValueTask DisposeAsync()
            {
                // 并发 MoveNext 时无效的释放会提前失败，此时不能解除仍在执行的预算占用。
                await inner.DisposeAsync().ConfigureAwait(false);
                stream.CloseScope();
            }
        }

        private void CloseScope()
        {
            // 旧流的延迟或重复释放不能解除后续模型请求持有的串行锁。
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { exactInput?.Dispose(); }
            finally { Volatile.Write(ref owner._active, 0); }
        }

        private async IAsyncEnumerable<ChatResponseUpdate> Enumerate([EnumeratorCancellation] CancellationToken enumerationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(dispatchToken, enumerationToken);
            IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
            long reportedOutput = 0;
            long reportedInput = 0;
            try
            {
                enumerator = CreateEnumerator(linked.Token);
                while (await MoveNextAsync(enumerator, linked.Token).ConfigureAwait(false))
                {
                    yield return ReadCurrent(enumerator, ref reportedInput, ref reportedOutput, linked.Token);
                }
            }
            finally
            {
                try { if (enumerator is not null) await DisposeEnumeratorAsync(enumerator).ConfigureAwait(false); }
                catch { owner._remainingTokens = 0; throw; }
                finally
                {
                    CloseScope();
                }
            }
        }

        private ChatResponseUpdate ReadCurrent(IAsyncEnumerator<ChatResponseUpdate> enumerator, ref long reportedInput, ref long reportedOutput,
            CancellationToken token)
        {
            using var activation = exactInput?.Enter();
            try
            {
                // Current 也可能执行提供者代码并失败；只读取一次，失败后不能继续派发。
                var update = enumerator.Current;
                token.ThrowIfCancellationRequested();
                if (update is null) throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    AiOperationUsage validated;
                    try { validated = AiModelUsagePolicy.Read(usage.Details); }
                    catch (AiBudgetException) { throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage"); }
                    var delta = validated.OutputTokens;
                    var inputDelta = validated.InputTokens;
                    if (delta > reservedTokens - reportedOutput
                        || inputDelta > AiExecutionBudget.MaximumTokens - reportedInput
                        || inputDelta > reservedInputTokens - reportedInput)
                        throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
                    if (delta.HasValue) reportedOutput += delta.Value;
                    if (inputDelta.HasValue) reportedInput += inputDelta.Value;
                }
                token.ThrowIfCancellationRequested();
                return update;
            }
            catch { owner._remainingTokens = 0; throw; }
        }

        private IAsyncEnumerator<ChatResponseUpdate> CreateEnumerator(CancellationToken cancellationToken)
        {
            using var activation = exactInput?.Enter();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return source.GetAsyncEnumerator(cancellationToken)
                    ?? throw new AiRunOutputBudgetException(true, "ai.budget.invalid_usage");
            }
            catch { owner._remainingTokens = 0; throw; }
        }

        private async Task<bool> MoveNextAsync(IAsyncEnumerator<ChatResponseUpdate> enumerator, CancellationToken cancellationToken)
        {
            using var activation = exactInput?.Enter();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                // 流的请求令牌与枚举令牌共同有效；忽略取消的流不得交付晚到数据。
                cancellationToken.ThrowIfCancellationRequested();
                return hasNext;
            }
            catch { owner._remainingTokens = 0; throw; }
        }

        private async ValueTask DisposeEnumeratorAsync(IAsyncEnumerator<ChatResponseUpdate> enumerator)
        {
            IDisposable? activation = null;
            try
            {
                try { activation = exactInput?.Enter(); }
                // 范围改变不得恢复旧权限，也不能阻止原枚举器清理；此时保留当前更严格或已失效的上下文。
                catch (AiBudgetException) { }
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            finally { activation?.Dispose(); }
        }
    }
}

/// <summary>区分输入／输出的本地派发前预算拒绝与派发后的不可信计量，避免把未执行意图误报为外部结果未知。</summary>
internal sealed class AiRunOutputBudgetException(bool dispatchStarted, string code = "ai.budget.limit_exceeded")
    : InvalidOperationException("Agent model budget rejected.")
{
    internal bool DispatchStarted { get; } = dispatchStarted;
    internal string Code { get; } = code;
}
