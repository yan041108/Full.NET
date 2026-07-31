namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 标识生成产物所属的技术边界，供后续写盘策略精确选择目标。
/// </summary>
public enum GeneratedArtifactKind
{
    /// <summary>后端 C# 契约或 SQL 代码。</summary>
    Backend = 1,

    /// <summary>Vue/TypeScript 客户端代码。</summary>
    VueClient = 2,

    /// <summary>Layui/JavaScript 客户端代码。</summary>
    LayuiClient = 3,

    /// <summary>供审查和自动化消费的生成报告。</summary>
    Report = 4,

    /// <summary>需要分配正式编号并通过双库评审后才能采用的迁移草案。</summary>
    MigrationTemplate = 5,

    /// <summary>需要随正式迁移落位后复制到 IntegrationTests 的验证草案。</summary>
    IntegrationTestTemplate = 6,
}

/// <summary>
/// 表示尚未写入目标工作区的确定性生成产物。
/// </summary>
/// <param name="RelativePath">使用正斜杠且不包含工作区绝对路径的目标相对路径。</param>
/// <param name="Kind">产物技术边界。</param>
/// <param name="Content">统一 LF 且以单个换行结束的文本内容。</param>
public sealed record GeneratedArtifact(
    string RelativePath,
    GeneratedArtifactKind Kind,
    string Content);
