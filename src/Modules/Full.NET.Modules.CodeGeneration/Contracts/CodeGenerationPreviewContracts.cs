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
    /// 模块错误码的通用前缀；所有具体错误码均以前缀 + '.' + 后缀拼接，避免跨模块冲突。
    /// </summary>
    public const string Prefix = "codegen.";

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
/// <param name="DeleteMode">删除模式稳定机器码，如 soft/hard/none；决定是否生成软删除筛选器。</param>
/// <param name="HasCreatedAudit">是否记录首次创建审计（创建者、创建时间）。</param>
/// <param name="HasUpdatedAudit">是否记录最近更新审计（更新者、更新时间）。</param>
/// <param name="HasDeletedAudit">是否记录软删除审计（删除者、删除时间）。</param>
/// <param name="HasVersion">是否启用乐观并发 Version 列；启用后所有变更请求必须携带 Version 参数。</param>
/// <param name="OwnershipMode">归属模式稳定机器码，如 host/tenant/user/none；决定查询时的默认数据范围过滤。</param>
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
/// <param name="PrincipalEntityKey">关系主端（一的一方）实体稳定键。</param>
/// <param name="PrincipalColumnName">主端被引用列名，通常是主键或唯一约束列。</param>
/// <param name="PrincipalDataScope">主端实体的数据作用域机器码，用于同租户/同归属校验。</param>
/// <param name="DependentEntityKey">关系从端（多的一方）实体稳定键。</param>
/// <param name="DependentColumnName">从端外键列名，该列类型必须与主端 PrincipalColumnName 兼容。</param>
/// <param name="DependentDataScope">从端实体的数据作用域机器码，必须与主端一致或为其子集。</param>
/// <param name="CompositeKeyColumnNames">复合主键的列名有序列表；当主端使用复合主键时必填。</param>
/// <param name="CascadeDelete">是否启用级联删除；null 表示保留数据库默认，true 明确启用，false 明确禁用。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationRelationshipRequest(
    string PrincipalEntityKey,
    string PrincipalColumnName,
    string PrincipalDataScope,
    string DependentEntityKey,
    string DependentColumnName,
    string DependentDataScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CompositeKeyColumnNames = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CascadeDelete = null);

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
/// <param name="Ui">可选展示元数据；缺省时不进入旧模板哈希。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationPreviewColumnRequest(
    string DatabaseName,
    string ClrPropertyName,
    string JsonPropertyName,
    string ScalarType,
    bool IsNullable,
    int? MaxLength,
    int? NumericPrecision,
    int? NumericScale,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CodeGenerationPreviewColumnUiRequest? Ui = null);

/// <summary>
/// 表示列的展示、表单与查询决策，不得改写物理列名。
/// </summary>
/// <param name="ControlKind">UI 控件类型稳定机器码，如 text/textarea/number/date/select/tag/switch 等。</param>
/// <param name="ShowInList">是否默认出现在列表视图列中。</param>
/// <param name="IncludeInCreate">是否出现在创建表单中。</param>
/// <param name="IncludeInUpdate">是否出现在编辑表单中。</param>
/// <param name="Required">前端提交表单时是否启用必填校验；与数据库 IsNullable 是不同抽象。</param>
/// <param name="Sortable">列表列是否允许点击排序。</param>
/// <param name="Queryable">是否出现在高级筛选条件面板中。</param>
/// <param name="QueryKind">查询比较方式稳定机器码，如 contains/equals/range/multi_select 等。</param>
/// <param name="Unique">前端表单是否启用异步唯一性校验（结合作用域）。</param>
/// <param name="IncludeInImportExport">是否参与导入导出模板字段列表。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationPreviewColumnUiRequest(
    string ControlKind,
    bool ShowInList,
    bool IncludeInCreate,
    bool IncludeInUpdate,
    bool Required,
    bool Sortable,
    bool Queryable,
    string QueryKind,
    bool Unique,
    bool IncludeInImportExport);

/// <summary>
/// 表示一次确定性的只读 CRUD 产物预览。
/// </summary>
/// <param name="DatabaseTableName">通过共享命名规则校验的表名。</param>
/// <param name="ReadPermission">生成的读取权限码。</param>
/// <param name="WritePermission">兼容写权限码；显式 Schema 等于 update。</param>
/// <param name="CreatePermission">生成的创建权限码。</param>
/// <param name="UpdatePermission">生成的更新权限码。</param>
/// <param name="DisablePermission">生成的停用或删除权限码。</param>
/// <param name="Artifacts">按相对路径稳定排序的内存产物。</param>
public sealed record CodeGenerationPreviewResponse(
    string DatabaseTableName,
    string ReadPermission,
    string WritePermission,
    IReadOnlyList<CodeGenerationPreviewArtifactResponse> Artifacts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CreatePermission = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UpdatePermission = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DisablePermission = null);

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
