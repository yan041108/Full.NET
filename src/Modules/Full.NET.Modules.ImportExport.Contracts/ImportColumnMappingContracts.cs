using System.Text.Json.Serialization;

namespace Full.NET.Modules.ImportExport.Contracts;

/// <summary>一个静态目标列对应文件首行中的一个字面表头，不承载字段转换或权限。</summary>
/// <param name="TargetColumn">代码注册的目标列机器名。</param>
/// <param name="SourceHeader">首尾空白规范化后、区分大小写的来源表头。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportColumnMapping(
    [property: JsonRequired] string TargetColumn,
    [property: JsonRequired] string SourceHeader);

/// <summary>提交时携带客户端看到的目标表头，来源变更后拒绝用旧结构猜测列含义。</summary>
/// <param name="HeaderColumns">客户端读取的完整有序目标表头。</param>
/// <param name="Columns">每个目标恰好一项且来源名称不重复的完整映射。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportColumnMappingRequest(
    [property: JsonRequired] IReadOnlyList<string> HeaderColumns,
    [property: JsonRequired] IReadOnlyList<ImportColumnMapping> Columns);

/// <summary>个人配置独立权限；目录与业务来源授权仍须分别检查。</summary>
public static class ImportProfilePermissions
{
    /// <summary>读取本人当前租户的导入配置。</summary>
    public const string Read = "import_export.import_profiles.read";
    /// <summary>创建本人的导入配置。</summary>
    public const string Create = "import_export.import_profiles.create";
    /// <summary>按版本更新本人的导入配置。</summary>
    public const string Update = "import_export.import_profiles.update";
    /// <summary>按版本删除本人的导入配置。</summary>
    public const string Delete = "import_export.import_profiles.delete";
}

/// <summary>跨业务工作簿消费者共用的稳定映射错误码。</summary>
public static class ImportColumnMappingErrorCodes
{
    /// <summary>映射配置或文件实际表头不合法。</summary>
    public const string Invalid = "import_export.column_mapping.invalid";
    /// <summary>当前静态目标表头与已读取或冻结的结构不同。</summary>
    public const string SchemaChanged = "import_export.column_mapping.schema_changed";
}

/// <summary>持久导入的会话和来源授权错误。</summary>
public static class ImportTaskAuthorityErrorCodes
{
    /// <summary>原排队会话、本人身份或来源所需权限不再成立。</summary>
    public const string Denied = "import_export.task.authority_denied";
}

/// <summary>本人导入配置的稳定错误码。</summary>
public static class ImportProfileErrorCodes
{
    /// <summary>名称、版本或配置不合法。</summary>
    public const string Invalid = "import_export.import_profiles.invalid";
    /// <summary>配置不存在或不属于当前本人作用域。</summary>
    public const string NotFound = "import_export.import_profiles.not_found";
    /// <summary>配置已被另一操作修改。</summary>
    public const string VersionConflict = "import_export.import_profiles.version_conflict";
    /// <summary>当前来源的个人配置数量已达到上限。</summary>
    public const string LimitReached = "import_export.import_profiles.limit_reached";
}

/// <summary>配置列表不返回列映射正文；指纹只表达目标表头结构。</summary>
/// <param name="Id">配置的应用端 UUID v7 标识。</param>
/// <param name="Name">本人定义的最多六十四字符名称。</param>
/// <param name="HeaderFingerprint">有序目标表头的 SHA-256 指纹。</param>
/// <param name="IsCompatible">指纹与当前目标表头一致；详情另检查正文有效性。</param>
/// <param name="CreatedAtUtc">创建 UTC 时间。</param>
/// <param name="UpdatedAtUtc">尚未修改时为空的最后更新 UTC 时间。</param>
/// <param name="Version">更新、删除时必须提交的正整数并发版本。</param>
public sealed record ImportProfileSummary(Guid Id, string Name, string HeaderFingerprint, bool IsCompatible,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Version);

/// <summary>只有兼容当前来源表头的配置才可应用；失效配置保留摘要供本人修正。</summary>
/// <param name="Summary">当前本人作用域中的摘要。</param>
/// <param name="Configuration">不兼容或正文损坏时为空，不得按旧位置自动应用。</param>
public sealed record ImportProfileDetail(ImportProfileSummary Summary, ImportColumnMappingRequest? Configuration);

/// <summary>创建版本为零；更新必须提供最近读取的非零版本。</summary>
/// <param name="Name">经首尾空白规范化的配置名称。</param>
/// <param name="Configuration">完整的当前目标表头与字面映射。</param>
/// <param name="Version">创建为零，更新必须匹配当前保存版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveImportProfileRequest([property: JsonRequired] string Name,
    [property: JsonRequired] ImportColumnMappingRequest Configuration, [property: JsonRequired] long Version);

/// <summary>删除采用显式版本，不能盲目覆盖其他窗口的修改。</summary>
/// <param name="Version">最近读取的正整数版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteImportProfileRequest([property: JsonRequired] long Version);
