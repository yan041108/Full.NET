using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Contracts;

/// <summary>
/// 定义 Host 代码生成模板目录的读写权限边界。
/// </summary>
public static class CodeGenerationTemplatePermissions
{
    /// <summary>读取代码生成模板列表与详情。</summary>
    public const string Read = "codegen.templates.read";

    /// <summary>创建新的代码生成模板。</summary>
    public const string Create = "codegen.templates.create";

    /// <summary>修改现有代码生成模板的名称、描述与 Schema。</summary>
    public const string Update = "codegen.templates.update";

    /// <summary>软删除代码生成模板，不影响已完成的 Run 结果。</summary>
    public const string Delete = "codegen.templates.delete";
}

/// <summary>
/// 定义 Host 代码生成模板目录的稳定错误码。
/// </summary>
public static class CodeGenerationTemplateErrorCodes
{
    /// <summary>模板输入不满足字段或长度边界。</summary>
    public const string Invalid = "codegen.template.invalid";

    /// <summary>模板标识不存在或已被删除。</summary>
    public const string NotFound = "codegen.template.not_found";

    /// <summary>乐观并发版本冲突：模板已被其他请求修改。</summary>
    public const string VersionConflict = "codegen.template.version_conflict";
}

/// <summary>
/// 表示创建一个 Host 代码生成模板的输入。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。<see cref="JsonUnmappedMemberHandling.Disallow"/> 拒绝未知字段，调用方必须按 Schema 升级；Schema 变更需要同步升级 SchemaSha256。</remarks>
/// <param name="Name">模板名称；用于人工识别，不影响生成行为。</param>
/// <param name="Description">模板描述；无描述时为 <see langword="null"/>。</param>
/// <param name="Schema">代码生成预览请求 Schema；决定模板输入与生成行为。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCodeGenerationTemplateRequest(
    string Name,
    string? Description,
    CodeGenerationPreviewRequest Schema);

/// <summary>
/// 表示以乐观并发方式更新 Host 代码生成模板的输入。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Version 必须匹配当前持久化版本以通过乐观并发；Schema 变更需要同步升级 SchemaSha256。</remarks>
/// <param name="Name">模板名称。</param>
/// <param name="Description">模板描述；传 <see langword="null"/> 表示清除描述。</param>
/// <param name="Schema">代码生成预览请求 Schema。</param>
/// <param name="Version">乐观锁版本；必须匹配当前持久化版本，否则冲突。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCodeGenerationTemplateRequest(
    string Name,
    string? Description,
    CodeGenerationPreviewRequest Schema,
    long Version);

/// <summary>
/// 表示以乐观并发方式软删除 Host 代码生成模板的输入。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。软删除不影响已完成的 Run 结果；Version 必须匹配当前持久化版本以通过乐观并发。</remarks>
/// <param name="Version">乐观锁版本；必须匹配当前持久化版本，否则冲突。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteCodeGenerationTemplateRequest(long Version);

/// <summary>
/// 表示一个已持久化且可重新预览的 Host 代码生成模板。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。SchemaSha256 用于检测 Schema 是否变更；Version 单调递增，作为乐观锁游标。</remarks>
/// <param name="Id">模板逻辑主键（应用端 UUID v7）。</param>
/// <param name="Name">模板名称。</param>
/// <param name="Description">模板描述；无描述时为 <see langword="null"/>。</param>
/// <param name="Schema">代码生成预览请求 Schema。</param>
/// <param name="SchemaSha256">Schema 内容的 SHA-256 摘要；用于检测 Schema 是否变更。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="CreatedByUserId">创建人用户标识。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="UpdatedByUserId">最近更新人用户标识；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观锁版本；单调递增。</param>
public sealed record CodeGenerationTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    CodeGenerationPreviewRequest Schema,
    string SchemaSha256,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version);
