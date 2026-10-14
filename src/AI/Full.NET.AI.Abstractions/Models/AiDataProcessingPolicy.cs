using System.Collections.Frozen;

namespace Full.NET.AI.Abstractions.Models;

/// <summary>宿主冻结的数据分类去向策略；不替代业务授权、精确模型批准或实际连接地址检查。</summary>
public sealed class AiDataProcessingPolicy
{
    private static readonly string[] Stages = ["chat", "embedding", "rerank", "context", "telemetry", "connectivity", "mcp"];
    private readonly FrozenDictionary<string, FrozenSet<string>> allowed;
    private readonly FrozenSet<string> privateClasses;

    /// <summary>兼容默认允许已知分类；没有分类证明的输入仍拒绝，私有化网络基线始终另行执行。</summary>
    public static AiDataProcessingPolicy Default { get; } = new();

    /// <summary>复制可信宿主策略；配置改变后须重启，不允许请求或模型修改集合。</summary>
    /// <param name="allowedClassifications">各固定阶段允许的分类；缺省阶段兼容已有批准，显式空集合拒绝该阶段。</param>
    /// <param name="privateClassifications">必须使用私网闭包的分类；缺省不增加业务批准之外的网络豁免。</param>
    public AiDataProcessingPolicy(IReadOnlyDictionary<string, IReadOnlyList<string>>? allowedClassifications = null,
        IReadOnlyList<string>? privateClassifications = null)
    {
        var entries = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal);
        if (allowedClassifications?.Keys.Any(stage => !Stages.Contains(stage, StringComparer.Ordinal)) == true) throw Invalid();
        foreach (var stage in Stages)
        {
            IReadOnlyList<string> values = ["public", "internal", "restricted"];
            if (allowedClassifications?.TryGetValue(stage, out var configured) == true)
                values = configured ?? throw Invalid();
            entries.Add(stage, Freeze(values));
        }
        allowed = entries.ToFrozenDictionary(StringComparer.Ordinal);
        privateClasses = Freeze(privateClassifications ?? Array.Empty<string>());
    }

    /// <summary>只接受封闭且大小写精确的分类，未知值不得降级到 public。</summary>
    /// <param name="classification">待检查分类。</param>
    /// <returns>是否为协议内已知分类。</returns>
    public static bool IsKnown(string? classification) => classification is "public" or "internal" or "restricted";

    /// <summary>按全部已授权来源取最严格分类；无低敏证明的问题或历史以 restricted 为下限。</summary>
    /// <param name="classifications">全部来源的权威分类。</param>
    /// <param name="minimum">服务端确定的最低敏感程度。</param>
    /// <returns>最严格的已知分类。</returns>
    public static string Strongest(IEnumerable<string> classifications, string minimum = "public")
    {
        ArgumentNullException.ThrowIfNull(classifications);
        if (!IsKnown(minimum)) throw Invalid();
        var result = minimum;
        foreach (var value in classifications)
        {
            if (!IsKnown(value)) throw Invalid();
            if (value == "restricted" || value == "internal" && result == "public") result = value;
        }
        return result;
    }

    /// <summary>检查固定处理阶段；返回值只约束去向，不授予模型调用或来源读取资格。</summary>
    /// <param name="stage">固定处理阶段。</param>
    /// <param name="classification">服务端分类。</param>
    /// <returns>该阶段是否允许此分类。</returns>
    public bool Allows(string stage, string classification) => IsKnown(classification)
        && allowed.TryGetValue(stage, out var values) && values.Contains(classification);

    /// <summary>阶段不获准时在读取凭据、创建客户端或本地正文处理前拒绝。</summary>
    /// <param name="stage">固定处理阶段。</param>
    /// <param name="classification">服务端分类。</param>
    public void Require(string stage, string classification)
    {
        if (!Allows(stage, classification)) throw new InvalidOperationException("AI data processing is blocked by host policy.");
    }

    /// <summary>私网要求只能收紧网络范围，不能把特殊地址或公网批准为私网。</summary>
    /// <param name="classification">服务端分类。</param>
    /// <returns>该分类是否必须使用私网闭包。</returns>
    public bool RequiresPrivateNetwork(string classification)
    {
        if (!IsKnown(classification)) throw Invalid();
        return privateClasses.Contains(classification);
    }

    private static FrozenSet<string> Freeze(IReadOnlyList<string> values)
    {
        if (values.Count > 3 || values.Any(value => !IsKnown(value)) || values.Distinct(StringComparer.Ordinal).Count() != values.Count) throw Invalid();
        return values.ToFrozenSet(StringComparer.Ordinal);
    }
    private static InvalidOperationException Invalid() => new("AI data policy requires known stages and exact classifications.");
}
