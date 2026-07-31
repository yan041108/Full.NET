using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Contracts;

/// <summary>
/// 定义代码生成预览能力使用的稳定权限码。
/// </summary>
public static class CodeGenerationPreviewPermissions
{
    /// <summary>
    /// 允许读取内存生成的 CRUD 产物预览。
    /// </summary>
    public const string Read = "codegen.previews.read";
}

/// <summary>
/// 定义代码生成预览能力对外返回的稳定错误码。
/// </summary>
public static class CodeGenerationErrorCodes
{
    /// <summary>
    /// 表示请求未通过命名、类型或 CRUD 不变量校验。
    /// </summary>
    public const string InvalidPreviewSchema = "codegen.preview.invalid_schema";
}

/// <summary>
/// 表示一次只读 CRUD 产物预览请求。
/// </summary>
/// <param name="OwnerKey">项目所有者键。</param>
/// <param name="ModuleKey">模块键。</param>
/// <param name="EntityKey">实体键。</param>
/// <param name="DatabaseTableName">显式数据库表名。</param>
/// <param name="RootNamespace">生成代码的根命名空间。</param>
/// <param name="ClrTypeName">实体 CLR 类型名。</param>
/// <param name="ApiResourceName">API 资源路径段。</param>
/// <param name="PermissionResourceName">权限资源段。</param>
/// <param name="DataScope">显式数据作用域机器码。</param>
/// <param name="HasVersion">是否生成乐观并发契约。</param>
/// <param name="Columns">显式字段集合。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationPreviewRequest(
    string OwnerKey,
    string ModuleKey,
    string EntityKey,
    string DatabaseTableName,
    string RootNamespace,
    string ClrTypeName,
    string ApiResourceName,
    string PermissionResourceName,
    string DataScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? HasVersion,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CodeGenerationEntityCapabilitiesRequest? EntityCapabilities = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Scene = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CodeGenerationRelationshipRequest>? Relationships = null);

/// <summary>
/// 保存无法从列结构安全推断的实体生命周期、审计、并发与归属能力。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationEntityCapabilitiesRequest(
    string DeleteMode,
    bool HasCreatedAudit,
    bool HasUpdatedAudit,
    bool HasDeletedAudit,
    bool HasVersion,
    string OwnershipMode);

/// <summary>
/// 保存跨实体关系两端已经显式确认的语义键、列名与数据作用域。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRelationshipRequest(
    string PrincipalEntityKey,
    string PrincipalColumnName,
    string PrincipalDataScope,
    string DependentEntityKey,
    string DependentColumnName,
    string DependentDataScope);

/// <summary>
/// 表示预览请求中的一个显式字段。
/// </summary>
/// <param name="DatabaseName">数据库列名。</param>
/// <param name="ClrPropertyName">CLR 属性名。</param>
/// <param name="JsonPropertyName">JSON 属性名。</param>
/// <param name="ScalarType">跨数据库逻辑标量类型机器码。</param>
/// <param name="IsNullable">字段是否可空。</param>
/// <param name="MaxLength">字符串最大长度。</param>
/// <param name="NumericPrecision">定点数总有效位数。</param>
/// <param name="NumericScale">定点数小数位数。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationPreviewColumnRequest(
    string DatabaseName,
    string ClrPropertyName,
    string JsonPropertyName,
    string ScalarType,
    bool IsNullable,
    int? MaxLength,
    int? NumericPrecision,
    int? NumericScale);

/// <summary>
/// 表示一次确定性的只读 CRUD 产物预览。
/// </summary>
/// <param name="DatabaseTableName">通过共享命名规则校验的表名。</param>
/// <param name="ReadPermission">生成的读取权限码。</param>
/// <param name="WritePermission">生成的写入权限码。</param>
/// <param name="Artifacts">按相对路径稳定排序的内存产物。</param>
public sealed record CodeGenerationPreviewResponse(
    string DatabaseTableName,
    string ReadPermission,
    string WritePermission,
    IReadOnlyList<CodeGenerationPreviewArtifactResponse> Artifacts);

/// <summary>
/// 表示一个尚未写入工作区的生成产物。
/// </summary>
/// <param name="Path">使用正斜杠的目标相对路径。</param>
/// <param name="Kind">产物技术边界的稳定机器码。</param>
/// <param name="Sha256">UTF-8 内容的小写 SHA-256 摘要。</param>
/// <param name="Content">生成的完整文本内容。</param>
public sealed record CodeGenerationPreviewArtifactResponse(
    string Path,
    string Kind,
    string Sha256,
    string Content);
