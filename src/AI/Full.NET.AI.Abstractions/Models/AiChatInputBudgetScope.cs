using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.AI.Abstractions.Models;

/// <summary>向同一次模型调用传递可信输入额度与派发前复核；嵌套只能收紧，释放后捕获的异步上下文失效。</summary>
public sealed class AiChatInputBudgetScope : IDisposable
{
    private static readonly AsyncLocal<AiChatInputBudgetScope?> Ambient = new();
    private readonly AiChatInputBudgetScope? parent;
    private readonly Func<CancellationToken, Task>? revalidate;
    private readonly int depth;
    private readonly bool captured;
    private readonly object activationGate = new();
    private int activations;
    private int disposed;

    private AiChatInputBudgetScope(long limit, Func<CancellationToken, Task>? revalidate, AiChatInputBudgetScope? parent, bool captured = false)
    {
        this.parent = parent; this.revalidate = revalidate; this.captured = captured; depth = (parent?.depth ?? 0) + 1;
        if (depth > 16) throw new InvalidOperationException("AI input admission nesting limit exceeded.");
        InputTokenLimit = Math.Min(limit, parent?.InputTokenLimit ?? limit);
    }

    /// <summary>当前调用继承全部父级后的输入 Token 上限。</summary>
    public long InputTokenLimit { get; }

    /// <summary>安装请求级额度；复核回调必须重新验证原会话、范围和当前资源，不能只返回缓存判断。</summary>
    /// <param name="inputTokenLimit">本次已预留的剩余输入额度，最多十亿 Token。</param>
    /// <param name="revalidate">计数前及生成前执行的权威复核；为空时仅继承父级，整条链没有复核则拒绝精确派发。</param>
    /// <returns>必须在原异步调用中按后进先出顺序释放的作用域。</returns>
    public static AiChatInputBudgetScope Begin(long inputTokenLimit, Func<CancellationToken, Task>? revalidate = null)
    {
        if (inputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens) throw new ArgumentOutOfRangeException(nameof(inputTokenLimit));
        var parent = Ambient.Value; parent?.EnsureActive();
        var scope = new AiChatInputBudgetScope(inputTokenLimit, revalidate, parent); Ambient.Value = scope; return scope;
    }

    /// <summary>捕获一次模型调用的额度和原父级，不安装到调用者；异步流须在每次提供程序操作期间激活。</summary>
    /// <param name="inputTokenLimit">本次已预留的剩余输入额度，最多十亿 Token。</param>
    /// <returns>由流所有者在全部提供程序操作清理后释放的调用范围。</returns>
    public static AiChatInputBudgetScope Capture(long inputTokenLimit)
    {
        if (inputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens) throw new ArgumentOutOfRangeException(nameof(inputTokenLimit));
        var parent = Ambient.Value; parent?.EnsureActive();
        return new(inputTokenLimit, null, parent, captured: true);
    }

    /// <summary>只在原父级且无其他激活时进入捕获范围；激活必须在同一异步操作内释放，不能跨越 yield。</summary>
    /// <returns>恢复原父级的短期激活凭据，不结束整个模型调用范围。</returns>
    public IDisposable Enter()
    {
        lock (activationGate)
        {
            EnsureActive(); parent?.EnsureActive();
            if (!captured || activations != 0 || !ReferenceEquals(Ambient.Value, parent)) throw Unavailable();
            activations = 1; Ambient.Value = this;
            return new Activation(this);
        }
    }

    /// <summary>取得当前可信调用，不能从缺失或已经释放的上下文推断无限预算。</summary>
    /// <returns>当前仍有效的调用作用域。</returns>
    public static AiChatInputBudgetScope RequireCurrent()
    {
        var scope = Ambient.Value ?? throw Unavailable(); scope.EnsureActive(); return scope;
    }

    /// <summary>复核全部父级资格；取消、释放或任何授权拒绝均阻止后续生成请求。</summary>
    /// <param name="cancellationToken">本次模型派发的原取消令牌。</param>
    public async Task RevalidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureActive(); var checkedAuthority = false;
        for (AiChatInputBudgetScope? scope = this; scope is not null; scope = scope.parent)
        {
            scope.EnsureActive();
            if (scope.revalidate is not null)
            {
                await scope.revalidate(cancellationToken).ConfigureAwait(false); checkedAuthority = true;
            }
            cancellationToken.ThrowIfCancellationRequested(); scope.EnsureActive(); EnsureActive();
        }
        if (!checkedAuthority) throw Unavailable();
        // 最外层回调等待期间，中间父作用域也可能被原所有者释放，返回前同步重验全部祖先。
        for (AiChatInputBudgetScope? ancestor = this; ancestor is not null; ancestor = ancestor.parent) ancestor.EnsureActive();
    }

    /// <summary>结束调用范围并使后台捕获失效；普通范围恢复父级，捕获范围必须先释放全部激活。</summary>
    public void Dispose()
    {
        if (captured)
        {
            lock (activationGate)
            {
                if (Volatile.Read(ref disposed) != 0) return;
                if (activations != 0) throw new InvalidOperationException("AI input activation must finish before closing the captured scope.");
                Volatile.Write(ref disposed, 1); return;
            }
        }
        if (Volatile.Read(ref disposed) != 0) return;
        if (!ReferenceEquals(Ambient.Value, this)) throw new InvalidOperationException("AI input scopes must be disposed in order.");
        Volatile.Write(ref disposed, 1); Ambient.Value = parent;
    }

    private sealed class Activation(AiChatInputBudgetScope owner) : IDisposable
    {
        private bool closed;
        public void Dispose()
        {
            lock (owner.activationGate)
            {
                if (closed) return;
                if (!ReferenceEquals(Ambient.Value, owner)) throw new InvalidOperationException("AI input activations must be disposed in order.");
                Ambient.Value = owner.parent; owner.activations = 0; closed = true;
            }
        }
    }

    private void EnsureActive() { if (Volatile.Read(ref disposed) != 0) throw Unavailable(); }
    private static AiBudgetException Unavailable() => new("ai.budget.hard_input_unavailable");
}
