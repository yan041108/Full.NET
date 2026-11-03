using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>一次已持久领取样例的操作身份；服务按请求作用域注册，不用于缓存授权或自动重派。</summary>
internal sealed class AiKnowledgeEvaluationOperationScope(ICurrentTenant tenant)
{
    private readonly AsyncLocal<Entry?> current = new();
    internal bool IsActive => current.Value is not null;

    /// <summary>调用者必须先持久领取样例并核对原会话；所有阶段身份在领取前已保存。</summary>
    internal IDisposable Enter(Guid actor, Guid session, Guid? tenantId, bool scoped, IReadOnlyDictionary<string, Guid> operations,
        Func<CancellationToken,Task> revalidate)
    {
        if (current.Value is not null || actor == Guid.Empty || session == Guid.Empty || tenantId != tenant.Id
            || scoped != AiKnowledgeScope.IsTenant(tenant) || operations.Count is < 1 or > 5
            || operations.Keys.Any(key => !Stages.Contains(key)) || operations.Values.Any(id => id == Guid.Empty || id.Version != 7)
            || operations.Values.Distinct().Count() != operations.Count) throw new InvalidDataException("评估原操作身份无效。");
        ArgumentNullException.ThrowIfNull(revalidate);
        var entry = new Entry(actor, session, tenantId, scoped, new Dictionary<string, Guid>(operations, StringComparer.Ordinal),revalidate);
        current.Value = entry; return new Lease(this, entry);
    }

    /// <summary>收费前、实际派发和交付前都核对原会话及当前批准集合；不缓存上次授权结果。</summary>
    internal Task RevalidateAsync(AiKnowledgeRetrievalSnapshot source,CancellationToken token)
    {
        if(current.Value is not {} entry)return Task.CompletedTask;
        if(entry.Closed||entry.Actor!=source.Actor||entry.TenantId!=source.TenantId||entry.Scoped!=source.IsTenant)throw new UnauthorizedAccessException();
        return entry.Revalidate(token);
    }

    /// <summary>一项阶段在同次执行中只可取号一次；缺少预先冻结的身份时不能回退生成新号。</summary>
    internal Guid Next(string stage, AiKnowledgeRetrievalSnapshot source)
    {
        var entry = current.Value ?? throw new InvalidOperationException("当前没有已领取的评估样例。");
        if (entry.Closed || entry.Actor != source.Actor || entry.TenantId != source.TenantId || entry.Scoped != source.IsTenant
            || entry.TenantId != tenant.Id || entry.Scoped != AiKnowledgeScope.IsTenant(tenant)
            || !entry.Operations.TryGetValue(stage, out var id) || !entry.Used.Add(stage))
            throw new InvalidDataException("评估阶段不能替换原身份或重复派发。");
        return id;
    }

    internal static readonly IReadOnlySet<string> Stages = new HashSet<string>(StringComparer.Ordinal)
    {
        "comparison_embedding", "refinement_original_embedding", "refinement_rewrite", "refinement_rewritten_embedding", "refinement_rerank"
    };
    private sealed record Entry(Guid Actor, Guid Session, Guid? TenantId, bool Scoped, IReadOnlyDictionary<string, Guid> Operations,Func<CancellationToken,Task> Revalidate)
    { internal HashSet<string> Used { get; } = new(StringComparer.Ordinal); internal volatile bool Closed; }
    private sealed class Lease(AiKnowledgeEvaluationOperationScope owner, Entry entry) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (!ReferenceEquals(owner.current.Value, entry)) throw new InvalidOperationException("评估原操作作用域已改变。");
            // 子异步流仍持有同一条目时必须拒绝晚到操作，不能因主流程退出而回退普通派发。
            entry.Closed=true;owner.current.Value = null;
        }
    }
}
