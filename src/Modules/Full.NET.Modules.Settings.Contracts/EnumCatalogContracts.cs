namespace Full.NET.Modules.Settings.Contracts;

/// <summary>
/// Host 枚举/常量元数据目录的权限与契约。
/// </summary>
public static class EnumCatalogPermissions
{
    /// <summary>查询枚举/常量目录列表与详情。</summary>
    public const string Read = "settings.enums.read";

    /// <summary>将已登记枚举目录预览并生成 Host 数据字典。</summary>
    public const string GenerateDict = "settings.enums.generate_dict";
}

/// <summary>模块向 Settings 注册稳定枚举/常量目录的贡献者。</summary>
public interface IEnumCatalogContributor
{
    /// <summary>获取当前模块贡献的稳定枚举/常量目录定义集合。</summary>
    IReadOnlyCollection<EnumCatalogDefinition> Catalogs { get; }
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>一个可查询的稳定枚举/常量目录定义。</summary>
/// <param name="Key">稳定目录键；发布后不可改名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">说明；可为空。</param>
/// <param name="Members">目录成员集合；成员机器码顺序发布后不可重排或删除。</param>
public sealed record EnumCatalogDefinition(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<EnumCatalogMemberDefinition> Members);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>目录内单个稳定成员。</summary>
/// <param name="Code">稳定机器码；发布后不可改名或删除。</param>
/// <param name="Label">中文展示标签。</param>
/// <param name="DisplayOrder">列表排序值；同级内决定展示顺序。</param>
public sealed record EnumCatalogMemberDefinition(
    string Code,
    string Label,
    int DisplayOrder);

/// <summary>枚举目录列表项。</summary>
/// <param name="Key">稳定目录键。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">说明；可为空。</param>
/// <param name="MemberCount">成员数量。</param>
public sealed record EnumCatalogSummary(
    string Key,
    string DisplayName,
    string? Description,
    int MemberCount);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>枚举目录详情（含成员）。</summary>
/// <param name="Key">稳定目录键。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">说明；可为空。</param>
/// <param name="Members">成员集合；顺序与定义一致。</param>
public sealed record EnumCatalogDetail(
    string Key,
    string DisplayName,
    string? Description,
    IReadOnlyList<EnumCatalogMember> Members);

/// <summary>枚举目录成员响应。</summary>
/// <param name="Code">稳定机器码。</param>
/// <param name="Label">中文展示标签。</param>
/// <param name="DisplayOrder">列表排序值。</param>
public sealed record EnumCatalogMember(
    string Code,
    string Label,
    int DisplayOrder);

/// <summary>枚举目录生成字典的逐行预览动作。</summary>
public static class EnumCatalogDictGenerationItemActions
{
    /// <summary>将新增字典项。</summary>
    public const string Create = "create";

    /// <summary>字典项已存在且与目录一致，跳过。</summary>
    public const string SkipExists = "skip_exists";

    /// <summary>字典项已存在但显示标签与目录不一致，保留人工数据。</summary>
    public const string ConflictLabel = "conflict_label";

    /// <summary>目录机器码不符合字典项值规则，无法生成。</summary>
    public const string InvalidValue = "invalid_value";
}

/// <summary>枚举目录生成 Host 字典的预览结果。</summary>
/// <param name="CatalogKey">源枚举目录键。</param>
/// <param name="DictTypeCode">目标字典类型编码，与目录键一致。</param>
/// <param name="DictTypeName">拟创建或已存在的字典类型显示名称。</param>
/// <param name="DictTypeExists">目标字典类型是否已存在。</param>
/// <param name="WillCreateDictType">执行后是否会新建字典类型。</param>
/// <param name="Items">逐成员生成计划。</param>
/// <param name="UnmanagedItems">字典中已存在但不在目录内的项，只读提示不删除。</param>
public sealed record EnumCatalogDictGenerationPreview(
    string CatalogKey,
    string DictTypeCode,
    string DictTypeName,
    bool DictTypeExists,
    bool WillCreateDictType,
    IReadOnlyList<EnumCatalogDictGenerationItemPreview> Items,
    IReadOnlyList<EnumCatalogDictGenerationUnmanagedItem> UnmanagedItems);

/// <summary>单个目录成员的字典生成预览。</summary>
/// <param name="Value">目录成员机器码；映射为字典项值。</param>
/// <param name="ProposedLabel">目录中声明的中文标签；用于新增或覆盖建议。</param>
/// <param name="ExistingLabel">字典中已存在的标签；不存在时为 <see langword="null"/>。</param>
/// <param name="DisplayOrder">字典项排序值。</param>
/// <param name="Action">生成动作稳定机器码，取值自 EnumCatalogDictGenerationItemActions。</param>
public sealed record EnumCatalogDictGenerationItemPreview(
    string Value,
    string ProposedLabel,
    string? ExistingLabel,
    int DisplayOrder,
    string Action);

/// <summary>字典中存在但目录未登记的项。</summary>
/// <param name="Value">字典项值；不属于任何目录成员。</param>
/// <param name="Label">字典项当前中文标签。</param>
/// <param name="IsActive">字典项是否仍启用；生成不会删除此项。</param>
public sealed record EnumCatalogDictGenerationUnmanagedItem(
    string Value,
    string Label,
    bool IsActive);

/// <summary>枚举目录生成 Host 字典的执行结果。</summary>
/// <param name="CatalogKey">源枚举目录键。</param>
/// <param name="DictTypeCode">目标字典类型编码，与目录键一致。</param>
/// <param name="DictTypeId">字典类型标识；新建成功后非空，已存在时为已有标识。</param>
/// <param name="DictTypeCreated">本次是否真正新建字典类型；已存在时为 <see langword="false"/>。</param>
/// <param name="ItemsCreated">新增字典项数量。</param>
/// <param name="ItemsSkipped">因一致跳过的字典项数量。</param>
/// <param name="ItemsConflicted">标签冲突未覆盖的字典项数量。</param>
/// <param name="ItemsInvalid">机器码无效未生成的字典项数量。</param>
/// <param name="Items">逐成员生成计划与结果；包含冲突与无效项用于复核。</param>
public sealed record EnumCatalogDictGenerationResult(
    string CatalogKey,
    string DictTypeCode,
    Guid? DictTypeId,
    bool DictTypeCreated,
    int ItemsCreated,
    int ItemsSkipped,
    int ItemsConflicted,
    int ItemsInvalid,
    IReadOnlyList<EnumCatalogDictGenerationItemPreview> Items);
