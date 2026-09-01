namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// 承载 Host 代码生成模板的持久化投影，不向 HTTP 契约暴露存储字段。
/// 确定性哈希：SchemaSha256 与持久化 SchemaJson 必须严格对应；加载后若重算不一致，立即 FAIL-closed 拒绝进入编辑器或 apply。
/// </summary>
internal sealed class CodeGenerationTemplateRecord
{
    /// <summary>模板稳定唯一标识；用于列表跳转、分享链接与运行侧外键引用。</summary>
    public Guid Id { get; init; }

    /// <summary>模板展示名称；用于列表与搜索，不参与生成也不影响摘要。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>模板可选说明文案；用于目录列表辅助说明，不参与生成哈希。</summary>
    public string? Description { get; init; }

    /// <summary>FullNetCrudSchema 的规范 JSON 文本；包含命名分段、列元数据与关系声明，是模板的权威载荷。</summary>
    public string SchemaJson { get; init; } = string.Empty;

    /// <summary>SchemaJson 内容的 SHA-256 摘要；用于编辑页乐观并发校验与 apply 前重放检测。</summary>
    public string SchemaSha256 { get; init; } = string.Empty;

    /// <summary>模板首次创建的 UTC 时间戳；用于排序与最近创建过滤。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>模板创建者用户 Id；用于权限校验与审计归属。</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>模板最近一次编辑保存的 UTC 时间戳；从未编辑时为空。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>最近一次编辑保存的用户 Id；从未编辑时为空。</summary>
    public Guid? UpdatedByUserId { get; init; }

    /// <summary>模板乐观并发版本号；每次保存自增 1，用于编辑页并发覆盖检测。</summary>
    public long Version { get; init; }
}
