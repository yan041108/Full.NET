using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Contracts;

/// <summary>
/// 定义 Host 代码生成模板目录的读写权限边界。
/// </summary>
public static class CodeGenerationTemplatePermissions
{
    public const string Read = "codegen.templates.read";

    public const string Write = "codegen.templates.write";
}

/// <summary>
/// 定义 Host 代码生成模板目录的稳定错误码。
/// </summary>
public static class CodeGenerationTemplateErrorCodes
{
    public const string Invalid = "codegen.template.invalid";

    public const string NotFound = "codegen.template.not_found";

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
