using Full.NET.AI.Abstractions.Budgets;
using System.Collections.Frozen;

namespace Full.NET.AI.Abstractions.Models;

/// <summary>宿主对精确 Chat 输入硬限的要求；估算、回执或字符上限不能认证请求前 Token 数。</summary>
public sealed class AiChatInputAdmissionPolicy
{
    private readonly FrozenSet<(string Provider, string Model)> capabilities;

    /// <summary>创建兼容的输入策略；没有可信能力声明时严格模式保持拒绝。</summary>
    /// <param name="requireExactTokens">是否强制派发前精确计量。</param>
    public AiChatInputAdmissionPolicy(bool requireExactTokens = false) : this(requireExactTokens, []) { }

    /// <summary>冻结宿主批准的精确计数能力；预留准入不替代实际端点、协议和输入计数复核。</summary>
    /// <param name="requireExactTokens">是否强制精确输入准入。</param>
    /// <param name="certifiedCapabilities">有限的精确供应商/模型配对，禁止通配。</param>
    public AiChatInputAdmissionPolicy(bool requireExactTokens, IReadOnlyCollection<AiExactChatCapability> certifiedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(certifiedCapabilities);
        if (certifiedCapabilities.Count > 512 || certifiedCapabilities.Any(entry => entry is null
            || string.IsNullOrWhiteSpace(entry.ProviderKey) || entry.ProviderKey.Length > 32 || entry.ProviderKey.Contains('*')
            || string.IsNullOrWhiteSpace(entry.ModelId) || entry.ModelId.Length > 256 || entry.ModelId.Contains('*')))
            throw new ArgumentException("AI exact input capabilities must be finite explicit bindings.", nameof(certifiedCapabilities));
        RequireExactTokens = requireExactTokens;
        capabilities = certifiedCapabilities.Select(entry => (entry.ProviderKey, entry.ModelId)).ToFrozenSet();
    }
    /// <summary>兼容已有预算预留；仍保留有界输入快照和派发后回执检查。</summary>
    public static AiChatInputAdmissionPolicy Default { get; } = new();

    /// <summary>宿主是否要求派发前的精确输入计量。</summary>
    public bool RequireExactTokens { get; }

    /// <summary>旧调用未提供协议能力证明，严格模式继续拒绝。</summary>
    public void RequireChatDispatch() => RequireChatDispatch(false);

    /// <summary>实际客户端只能在可信宿主批准的精确计数协议上进入严格模式。</summary>
    /// <param name="supportsExactInput">当前精确端点与模型是否使用已批准的计数/生成协议。</param>
    public void RequireChatDispatch(bool supportsExactInput)
    {
        if (RequireExactTokens && !supportsExactInput) throw new AiBudgetException("ai.budget.hard_input_unavailable");
    }

    /// <summary>预留前拒绝没有计数能力的模型；真实输入是否超量由派发边界继续核验。</summary>
    /// <param name="providerKey">已授权模型的静态供应商键。</param>
    /// <param name="modelId">已授权模型的精确标识。</param>
    public void RequireBudgetReservation(string providerKey, string modelId)
    {
        if (RequireExactTokens && !capabilities.Contains((providerKey, modelId))) throw new AiBudgetException("ai.budget.hard_input_unavailable");
    }
}

/// <summary>可信宿主声明的精确输入能力，不授予任何模型或数据访问权。</summary>
/// <param name="ProviderKey">静态供应商键。</param><param name="ModelId">精确模型标识，禁止通配。</param>
public sealed record AiExactChatCapability(string ProviderKey, string ModelId);
