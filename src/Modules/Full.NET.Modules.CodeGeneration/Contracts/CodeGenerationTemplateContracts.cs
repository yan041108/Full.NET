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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCodeGenerationTemplateRequest(
    string Name,
    string? Description,
    CodeGenerationPreviewRequest Schema);

/// <summary>
/// 表示以乐观并发方式更新 Host 代码生成模板的输入。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCodeGenerationTemplateRequest(
    string Name,
    string? Description,
    CodeGenerationPreviewRequest Schema,
    long Version);

/// <summary>
/// 表示以乐观并发方式软删除 Host 代码生成模板的输入。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteCodeGenerationTemplateRequest(long Version);

/// <summary>
/// 表示一个已持久化且可重新预览的 Host 代码生成模板。
/// </summary>
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
