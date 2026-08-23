namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存数据库元数据无法推导的 CRUD 契约、数据作用域和实体能力。
/// </summary>
public sealed record DatabaseCrudImportOptions
{
    /// <summary>使用兼容租户标记和版本标记创建数据库导入选项。</summary>
    public DatabaseCrudImportOptions(
        string OwnerKey,
        string ModuleKey,
        string EntityKey,
        string RootNamespace,
        string ClrTypeName,
        string ApiResourceName,
        string PermissionResourceName,
        bool IsTenantScoped,
        bool HasVersion)
        : this(
            OwnerKey,
            ModuleKey,
            EntityKey,
            RootNamespace,
            ClrTypeName,
            ApiResourceName,
            PermissionResourceName,
            IsTenantScoped
                ? FullNetCrudDataScope.TenantRequired
                : FullNetCrudDataScope.Unspecified,
            HasVersion)
    {
    }

    /// <summary>使用显式数据作用域和兼容版本标记创建数据库导入选项。</summary>
    public DatabaseCrudImportOptions(
        string OwnerKey,
        string ModuleKey,
        string EntityKey,
        string RootNamespace,
        string ClrTypeName,
        string ApiResourceName,
        string PermissionResourceName,
        FullNetCrudDataScope DataScope,
        bool HasVersion)
    {
        this.OwnerKey = OwnerKey;
        this.ModuleKey = ModuleKey;
        this.EntityKey = EntityKey;
        this.RootNamespace = RootNamespace;
        this.ClrTypeName = ClrTypeName;
        this.ApiResourceName = ApiResourceName;
        this.PermissionResourceName = PermissionResourceName;
        this.DataScope = DataScope;
        this.HasVersion = HasVersion;
        EntityCapabilities =
            FullNetCrudEntityCapabilities.FromLegacy(HasVersion);
        UsesLegacyEntityCapabilities = true;
    }

    /// <summary>使用显式数据作用域和实体能力创建数据库导入选项。</summary>
    public DatabaseCrudImportOptions(
        string OwnerKey,
        string ModuleKey,
        string EntityKey,
        string RootNamespace,
        string ClrTypeName,
        string ApiResourceName,
        string PermissionResourceName,
        FullNetCrudDataScope DataScope,
        FullNetCrudEntityCapabilities EntityCapabilities)
    {
        ArgumentNullException.ThrowIfNull(EntityCapabilities);
        this.OwnerKey = OwnerKey;
        this.ModuleKey = ModuleKey;
        this.EntityKey = EntityKey;
        this.RootNamespace = RootNamespace;
        this.ClrTypeName = ClrTypeName;
        this.ApiResourceName = ApiResourceName;
        this.PermissionResourceName = PermissionResourceName;
        this.DataScope = DataScope;
        HasVersion = EntityCapabilities.HasVersion;
        this.EntityCapabilities = EntityCapabilities;
        UsesLegacyEntityCapabilities = false;
    }

    /// <summary>获取冻结的项目所有权键。</summary>
    public string OwnerKey { get; }

    /// <summary>获取模块键。</summary>
    public string ModuleKey { get; }

    /// <summary>获取实体键。</summary>
    public string EntityKey { get; }

    /// <summary>获取生成 C# 类型使用的根命名空间。</summary>
    public string RootNamespace { get; }

    /// <summary>获取实体 CLR 类型名。</summary>
    public string ClrTypeName { get; }

    /// <summary>获取 API 集合资源路径分段。</summary>
    public string ApiResourceName { get; }

    /// <summary>获取权限码资源分段。</summary>
    public string PermissionResourceName { get; }

    /// <summary>获取是否要求可信租户上下文；等价于 DataScope 为 TenantRequired。</summary>
    public bool IsTenantScoped =>
        DataScope == FullNetCrudDataScope.TenantRequired;

    /// <summary>获取是否使用 Version 乐观并发。</summary>
    public bool HasVersion { get; }

    /// <summary>获取数据库导入已经显式确认的数据作用域。</summary>
    public FullNetCrudDataScope DataScope { get; }

    /// <summary>获取显式或兼容映射后的实体能力。</summary>
    public FullNetCrudEntityCapabilities EntityCapabilities { get; }

    /// <summary>获取实体能力是否来自旧版 HasVersion 输入。</summary>
    public bool UsesLegacyEntityCapabilities { get; }
}
