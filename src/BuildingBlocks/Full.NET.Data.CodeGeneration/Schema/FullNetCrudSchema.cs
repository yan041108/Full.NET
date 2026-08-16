using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 表示已经通过共享命名与 CRUD 不变量校验的生成输入。
/// </summary>
public sealed class FullNetCrudSchema
{
    private FullNetCrudSchema(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        FullNetCrudDataScope dataScope,
        FullNetCrudEntityCapabilities entityCapabilities,
        bool usesLegacyEntityCapabilities,
        FullNetCrudScene scene,
        IReadOnlyList<FullNetCrudRelationship> relationships,
        IReadOnlyList<FullNetColumn> columns)
    {
        OwnerKey = ownerKey;
        ModuleKey = moduleKey;
        EntityKey = entityKey;
        DatabaseTableName = databaseTableName;
        RootNamespace = rootNamespace;
        ClrTypeName = clrTypeName;
        ApiResourceName = apiResourceName;
        PermissionResourceName = permissionResourceName;
        DataScope = dataScope;
        IsTenantScoped = dataScope == FullNetCrudDataScope.TenantRequired;
        EntityCapabilities = entityCapabilities;
        UsesLegacyEntityCapabilities = usesLegacyEntityCapabilities;
        Scene = scene;
        Relationships = relationships;
        Columns = columns;
        ReadPermission = $"{moduleKey}.{permissionResourceName}.read";
        CreatePermission = $"{moduleKey}.{permissionResourceName}.create";
        UpdatePermission = $"{moduleKey}.{permissionResourceName}.update";
        DisablePermission = $"{moduleKey}.{permissionResourceName}.disable";
        // 遗留 hasVersion Schema 继续发出 .write；显式能力把兼容字段对齐到 update。
        WritePermission = usesLegacyEntityCapabilities
            ? $"{moduleKey}.{permissionResourceName}.write"
            : UpdatePermission;
    }

    /// <summary>获取冻结的项目所有权键。</summary>
    public string OwnerKey { get; }

    /// <summary>获取模块键。</summary>
    public string ModuleKey { get; }

    /// <summary>获取实体键。</summary>
    public string EntityKey { get; }

    /// <summary>获取显式物理表名。</summary>
    public string DatabaseTableName { get; }

    /// <summary>获取生成 C# 类型使用的根命名空间。</summary>
    public string RootNamespace { get; }

    /// <summary>获取实体 CLR 类型名。</summary>
    public string ClrTypeName { get; }

    /// <summary>获取 API 集合资源路径分段。</summary>
    public string ApiResourceName { get; }

    /// <summary>获取权限码资源分段。</summary>
    public string PermissionResourceName { get; }

    /// <summary>获取只读权限码。</summary>
    public string ReadPermission { get; }

    /// <summary>获取创建权限码。</summary>
    public string CreatePermission { get; }

    /// <summary>获取更新权限码。</summary>
    public string UpdatePermission { get; }

    /// <summary>获取停用或删除权限码。</summary>
    public string DisablePermission { get; }

    /// <summary>
    /// 获取兼容写权限码。遗留 Schema 为 <c>.write</c>；显式 Schema 等于 <see cref="UpdatePermission"/>。
    /// </summary>
    public string WritePermission { get; }

    /// <summary>获取是否要求可信租户上下文。</summary>
    public bool IsTenantScoped { get; }

    /// <summary>获取已经显式确认的数据访问作用域。</summary>
    public FullNetCrudDataScope DataScope { get; }

    /// <summary>获取是否使用 Version 乐观并发。</summary>
    public bool HasVersion => EntityCapabilities.HasVersion;

    /// <summary>获取显式或兼容映射后的实体能力。</summary>
    public FullNetCrudEntityCapabilities EntityCapabilities { get; }

    /// <summary>获取实体能力是否来自旧版 hasVersion 输入。</summary>
    public bool UsesLegacyEntityCapabilities { get; }

    /// <summary>获取实体已经显式声明的生成场景。</summary>
    public FullNetCrudScene Scene { get; }

    /// <summary>获取按输入顺序冻结的关系两端声明。</summary>
    public IReadOnlyList<FullNetCrudRelationship> Relationships { get; }

    /// <summary>获取按确认顺序冻结的字段集合。</summary>
    public IReadOnlyList<FullNetColumn> Columns { get; }

    /// <summary>
    /// 创建具体项目的 CRUD Schema；所有派生名称必须与显式确认值完全一致。
    /// </summary>
    public static FullNetCrudSchema CreateProject(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        bool isTenantScoped,
        bool hasVersion,
        IReadOnlyList<FullNetColumn> columns) =>
        CreateProject(
            ownerKey,
            moduleKey,
            entityKey,
            databaseTableName,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            isTenantScoped
                ? FullNetCrudDataScope.TenantRequired
                : FullNetCrudDataScope.Unspecified,
            hasVersion,
            columns);

    /// <summary>
    /// 使用显式数据访问作用域创建项目 CRUD Schema。
    /// </summary>
    public static FullNetCrudSchema CreateProject(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        FullNetCrudDataScope dataScope,
        bool hasVersion,
        IReadOnlyList<FullNetColumn> columns) =>
        CreateProjectCore(
            ownerKey,
            moduleKey,
            entityKey,
            databaseTableName,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            dataScope,
            FullNetCrudEntityCapabilities.FromLegacy(hasVersion),
            usesLegacyEntityCapabilities: true,
            FullNetCrudScene.Single,
            [],
            columns);

    /// <summary>
    /// 使用显式数据访问作用域和实体能力创建项目 CRUD Schema。
    /// </summary>
    public static FullNetCrudSchema CreateProject(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        FullNetCrudDataScope dataScope,
        FullNetCrudEntityCapabilities entityCapabilities,
        IReadOnlyList<FullNetColumn> columns) =>
        CreateProjectCore(
            ownerKey,
            moduleKey,
            entityKey,
            databaseTableName,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            dataScope,
            entityCapabilities,
            usesLegacyEntityCapabilities: false,
            FullNetCrudScene.Single,
            [],
            columns);

    /// <summary>
    /// 使用显式数据作用域、实体能力和交互场景创建项目 CRUD Schema。
    /// </summary>
    public static FullNetCrudSchema CreateProject(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        FullNetCrudDataScope dataScope,
        FullNetCrudEntityCapabilities entityCapabilities,
        FullNetCrudScene scene,
        IReadOnlyList<FullNetCrudRelationship> relationships,
        IReadOnlyList<FullNetColumn> columns) =>
        CreateProjectCore(
            ownerKey,
            moduleKey,
            entityKey,
            databaseTableName,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            dataScope,
            entityCapabilities,
            usesLegacyEntityCapabilities: false,
            scene,
            relationships,
            columns);

    private static FullNetCrudSchema CreateProjectCore(
        string ownerKey,
        string moduleKey,
        string entityKey,
        string databaseTableName,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName,
        FullNetCrudDataScope dataScope,
        FullNetCrudEntityCapabilities entityCapabilities,
        bool usesLegacyEntityCapabilities,
        FullNetCrudScene scene,
        IReadOnlyList<FullNetCrudRelationship> relationships,
        IReadOnlyList<FullNetColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(entityCapabilities);
        Ensure(
            Enum.IsDefined(dataScope),
            "数据访问作用域不受支持。",
            nameof(dataScope));
        ValidateEntityCapabilities(entityCapabilities);
        Ensure(
            Enum.IsDefined(scene),
            "CRUD 生成场景不受支持。",
            nameof(scene));
        var expectedTableName = SchemaName.CreateProject(
            ownerKey,
            moduleKey,
            entityKey).Value;
        if (!string.Equals(
            databaseTableName,
            expectedTableName,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "显式表名与共享 Naming Profile 计算结果不一致。",
                nameof(databaseTableName));
        }

        EnsureRootNamespace(rootNamespace);
        Ensure(
            ContractNameValidator.IsValidDotNetType(clrTypeName),
            "CLR 类型名不符合 Naming Profile。",
            nameof(clrTypeName));
        Ensure(
            ContractNameValidator.IsValidHttpPathSegment(apiResourceName),
            "API 资源名不符合 Naming Profile。",
            nameof(apiResourceName));

        var readPermission = $"{moduleKey}.{permissionResourceName}.read";
        var writePermission = $"{moduleKey}.{permissionResourceName}.write";
        Ensure(
            ContractNameValidator.IsValidPermission(readPermission)
            && ContractNameValidator.IsValidPermission(writePermission),
            "权限资源名不能生成规范权限码。",
            nameof(permissionResourceName));

        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(relationships);
        Ensure(columns.Count > 0, "Schema 至少需要一个字段。", nameof(columns));
        var frozenColumns = columns.ToArray();
        var frozenRelationships = relationships.ToArray();
        ValidateColumns(
            frozenColumns,
            dataScope,
            entityCapabilities,
            usesLegacyEntityCapabilities);
        ValidateScene(
            entityKey,
            dataScope,
            scene,
            frozenRelationships,
            frozenColumns);

        return new FullNetCrudSchema(
            ownerKey,
            moduleKey,
            entityKey,
            databaseTableName,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            dataScope,
            entityCapabilities,
            usesLegacyEntityCapabilities,
            scene,
            Array.AsReadOnly(frozenRelationships),
            Array.AsReadOnly(frozenColumns));
    }

    private static void ValidateColumns(
        IReadOnlyList<FullNetColumn> columns,
        FullNetCrudDataScope dataScope,
        FullNetCrudEntityCapabilities entityCapabilities,
        bool usesLegacyEntityCapabilities)
    {
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column);
            Ensure(
                ContractNameValidator.IsValidColumn(column.DatabaseName),
                "数据库列名不符合 Naming Profile。",
                nameof(columns));
            Ensure(
                ContractNameValidator.IsValidDotNetType(column.ClrPropertyName),
                "CLR 属性名不符合 Naming Profile。",
                nameof(columns));
            Ensure(
                ContractNameValidator.IsValidJsonProperty(column.JsonPropertyName),
                "JSON 属性名不符合 Naming Profile。",
                nameof(columns));
            Ensure(
                column.ScalarType == FullNetScalarType.String
                    ? column.MaxLength is > 0
                    : column.MaxLength is null,
                "只有字符串字段允许声明正数 MaxLength。",
                nameof(columns));
            Ensure(
                column.ScalarType == FullNetScalarType.Decimal
                    ? column.NumericPrecision is >= 1 and <= 38
                        && column.NumericScale is >= 0
                        && column.NumericScale <= column.NumericPrecision
                    : column.NumericPrecision is null
                        && column.NumericScale is null,
                "Decimal 字段必须声明可跨双库使用的 NumericPrecision/NumericScale，非 Decimal 字段不得声明。",
                nameof(columns));
            Ensure(
                column.ScalarType == FullNetScalarType.DateTimeUtc
                    ? column.ClrPropertyName.EndsWith("Utc", StringComparison.Ordinal)
                    : !column.ClrPropertyName.EndsWith("Utc", StringComparison.Ordinal),
                "UTC 时间字段的逻辑类型与 Utc 后缀必须一致。",
                nameof(columns));
        }

        EnsureUnique(columns, column => column.DatabaseName, "数据库列名");
        EnsureUnique(columns, column => column.ClrPropertyName, "CLR 属性名");
        EnsureUnique(columns, column => column.JsonPropertyName, "JSON 属性名");
        EnsureRequiredColumn(columns, "Id", FullNetScalarType.Uuid);
        if (usesLegacyEntityCapabilities)
        {
            EnsureRequiredColumn(columns, "IsActive", FullNetScalarType.Boolean);
        }
        if (dataScope == FullNetCrudDataScope.TenantRequired)
        {
            EnsureRequiredColumn(columns, "TenantId", FullNetScalarType.Uuid);
        }
        else if (dataScope is FullNetCrudDataScope.HostOnly
            or FullNetCrudDataScope.Global)
        {
            Ensure(
                columns.All(column => column.DatabaseName != "TenantId"),
                "显式 HostOnly 或 Global Schema 不得包含 TenantId。",
                nameof(columns));
        }

        if (entityCapabilities.HasVersion)
        {
            EnsureRequiredColumn(columns, "Version", FullNetScalarType.Int64);
        }
        else if (!usesLegacyEntityCapabilities)
        {
            EnsureColumnsAbsent(columns, "Version");
        }

        if (usesLegacyEntityCapabilities)
        {
            return;
        }

        if (entityCapabilities.OwnershipMode
            == FullNetCrudOwnershipMode.OrganizationUnit)
        {
            Ensure(
                dataScope == FullNetCrudDataScope.TenantRequired,
                "OrganizationUnit 归属要求 TenantRequired 数据作用域；"
                + "HostOnly 与 Global 不得声明组织列。",
                nameof(dataScope));
        }

        ValidateCapabilityColumns(columns, entityCapabilities);
    }

    private static void ValidateScene(
        string entityKey,
        FullNetCrudDataScope dataScope,
        FullNetCrudScene scene,
        IReadOnlyList<FullNetCrudRelationship> relationships,
        IReadOnlyList<FullNetColumn> columns)
    {
        foreach (var relationship in relationships)
        {
            ArgumentNullException.ThrowIfNull(relationship);
            Ensure(
                IsEntityKey(relationship.PrincipalEntityKey)
                && IsEntityKey(relationship.DependentEntityKey),
                "关系两端的实体键必须是规范 lower_snake 标识。",
                nameof(relationships));
            Ensure(
                ContractNameValidator.IsValidColumn(
                    relationship.PrincipalColumnName)
                && ContractNameValidator.IsValidColumn(
                    relationship.DependentColumnName),
                "关系两端的列名必须符合 Naming Profile。",
                nameof(relationships));
            Ensure(
                Enum.IsDefined(relationship.PrincipalDataScope)
                && Enum.IsDefined(relationship.DependentDataScope)
                && relationship.PrincipalDataScope
                    != FullNetCrudDataScope.Unspecified
                && relationship.DependentDataScope
                    != FullNetCrudDataScope.Unspecified,
                "关系两端必须声明可执行的数据作用域。",
                nameof(relationships));
            Ensure(
                relationship.PrincipalDataScope
                    == relationship.DependentDataScope,
                "禁止生成跨数据作用域关系。",
                nameof(relationships));
            Ensure(
                relationship.PrincipalDataScope == dataScope,
                "关系作用域必须与当前实体作用域一致。",
                nameof(relationships));
            Ensure(
                relationship.PrincipalEntityKey == entityKey
                || relationship.DependentEntityKey == entityKey,
                "关系至少一端必须引用当前实体。",
                nameof(relationships));
            Ensure(
                relationship.PrincipalEntityKey
                    != relationship.DependentEntityKey,
                "自引用层级必须使用 Tree 场景，不得声明为跨实体关系。",
                nameof(relationships));

            var currentColumnName =
                relationship.PrincipalEntityKey == entityKey
                    ? relationship.PrincipalColumnName
                    : relationship.DependentColumnName;
            EnsureRequiredColumn(
                columns,
                currentColumnName,
                FullNetScalarType.Uuid,
                columns.SingleOrDefault(column =>
                    column.DatabaseName == currentColumnName)?.IsNullable
                    ?? false);
        }

        switch (scene)
        {
            case FullNetCrudScene.Single:
                Ensure(
                    relationships.Count == 0,
                    "Single 场景不得声明跨实体关系。",
                    nameof(relationships));
                break;
            case FullNetCrudScene.Tree:
                Ensure(
                    relationships.Count == 0,
                    "Tree 场景通过 ParentId 表达自引用，不得声明跨实体关系。",
                    nameof(relationships));
                EnsureRequiredColumn(
                    columns,
                    "ParentId",
                    FullNetScalarType.Uuid,
                    isNullable: true);
                break;
            case FullNetCrudScene.MasterDetail:
                Ensure(
                    relationships.Count == 1,
                    "MasterDetail 场景必须声明且仅声明一条关系。",
                    nameof(relationships));
                break;
            case FullNetCrudScene.ManyToMany:
                Ensure(
                    relationships.Count == 2
                    && relationships.All(relationship =>
                        relationship.DependentEntityKey == entityKey)
                    && relationships
                        .Select(relationship =>
                            relationship.PrincipalEntityKey)
                        .Distinct(StringComparer.Ordinal)
                        .Count() == 2,
                    "ManyToMany 场景必须声明当前关联实体到两个不同主端的关系。",
                    nameof(relationships));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scene),
                    scene,
                    "CRUD 生成场景不受支持。");
        }
    }

    private static void ValidateEntityCapabilities(
        FullNetCrudEntityCapabilities entityCapabilities)
    {
        Ensure(
            Enum.IsDefined(entityCapabilities.DeleteMode),
            "实体删除模式不受支持。",
            nameof(entityCapabilities));
        Ensure(
            Enum.IsDefined(entityCapabilities.OwnershipMode),
            "实体所有权模式不受支持。",
            nameof(entityCapabilities));
        Ensure(
            entityCapabilities.DeleteMode != FullNetCrudDeleteMode.Immutable
            || (!entityCapabilities.HasUpdatedAudit
                && !entityCapabilities.HasDeletedAudit
                && !entityCapabilities.HasVersion),
            "不可变实体不得声明更新审计、删除审计或乐观并发。",
            nameof(entityCapabilities));
        Ensure(
            entityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete
            || !entityCapabilities.HasDeletedAudit,
            "只有软删除实体允许声明删除审计。",
            nameof(entityCapabilities));
    }

    private static void ValidateCapabilityColumns(
        IReadOnlyList<FullNetColumn> columns,
        FullNetCrudEntityCapabilities entityCapabilities)
    {
        if (entityCapabilities.CanUpdate)
        {
            Ensure(
                entityCapabilities.HasUpdatedAudit
                || entityCapabilities.HasVersion
                || columns.Any(column => !IsServerManagedColumn(
                    column.DatabaseName)),
                "可更新实体必须至少声明一个业务可写字段、更新审计或 Version。",
                nameof(columns));
        }

        if (entityCapabilities.HasCreatedAudit)
        {
            EnsureRequiredColumn(
                columns,
                "CreatedAtUtc",
                FullNetScalarType.DateTimeUtc);
            EnsureRequiredColumn(
                columns,
                "CreatedById",
                FullNetScalarType.Uuid);
        }
        else
        {
            EnsureColumnsAbsent(
                columns,
                "CreatedAtUtc",
                "CreatedById");
        }

        if (entityCapabilities.HasUpdatedAudit)
        {
            EnsureRequiredColumn(
                columns,
                "UpdatedAtUtc",
                FullNetScalarType.DateTimeUtc,
                isNullable: true);
            EnsureRequiredColumn(
                columns,
                "UpdatedById",
                FullNetScalarType.Uuid,
                isNullable: true);
        }
        else
        {
            EnsureColumnsAbsent(
                columns,
                "UpdatedAtUtc",
                "UpdatedById");
        }

        if (entityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete)
        {
            EnsureRequiredColumn(
                columns,
                "IsDeleted",
                FullNetScalarType.Boolean);
            if (entityCapabilities.HasDeletedAudit)
            {
                EnsureRequiredColumn(
                    columns,
                    "DeletedAtUtc",
                    FullNetScalarType.DateTimeUtc,
                    isNullable: true);
                EnsureRequiredColumn(
                    columns,
                    "DeletedById",
                    FullNetScalarType.Uuid,
                    isNullable: true);
            }
            else
            {
                EnsureColumnsAbsent(
                    columns,
                    "DeletedAtUtc",
                    "DeletedById");
            }
        }
        else
        {
            EnsureColumnsAbsent(
                columns,
                "IsDeleted",
                "DeletedAtUtc",
                "DeletedById");
        }

        if (entityCapabilities.OwnershipMode
            == FullNetCrudOwnershipMode.OrganizationUnit)
        {
            EnsureRequiredColumn(
                columns,
                "OrganizationUnitId",
                FullNetScalarType.Uuid);
        }
        else
        {
            EnsureColumnsAbsent(
                columns,
                "OrganizationUnitId");
        }
    }

    private static bool IsServerManagedColumn(string databaseName) =>
        databaseName is
            "Id"
            or "TenantId"
            or "Version"
            or "CreatedAtUtc"
            or "CreatedById"
            or "UpdatedAtUtc"
            or "UpdatedById"
            or "IsDeleted"
            or "DeletedAtUtc"
            or "DeletedById"
            or "OrganizationUnitId";

    private static void EnsureRequiredColumn(
        IEnumerable<FullNetColumn> columns,
        string name,
        FullNetScalarType scalarType,
        bool isNullable = false)
    {
        var column = columns.SingleOrDefault(item =>
            string.Equals(item.DatabaseName, name, StringComparison.Ordinal));
        Ensure(
            column is not null
            && column.ClrPropertyName == name
            && column.ScalarType == scalarType
            && column.IsNullable == isNullable,
            $"{name} 字段缺失或类型、可空性不符合 CRUD 不变量。",
            nameof(columns));
    }

    private static void EnsureColumnsAbsent(
        IEnumerable<FullNetColumn> columns,
        params string[] names)
    {
        var forbiddenNames = names.ToHashSet(StringComparer.Ordinal);
        Ensure(
            columns.All(column =>
                !forbiddenNames.Contains(column.DatabaseName)),
            $"当前实体能力不允许字段：{string.Join(", ", names)}。",
            nameof(columns));
    }

    private static void EnsureUnique(
        IEnumerable<FullNetColumn> columns,
        Func<FullNetColumn, string> selector,
        string label)
    {
        var values = columns.Select(selector).ToArray();
        Ensure(
            values.Distinct(StringComparer.Ordinal).Count() == values.Length,
            $"{label}必须唯一。",
            nameof(columns));
    }

    private static void EnsureRootNamespace(string rootNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);
        var segments = rootNamespace.Split('.', StringSplitOptions.None);
        Ensure(
            segments.Length > 0
            && segments.All(ContractNameValidator.IsValidDotNetType),
            "根命名空间不符合 Naming Profile。",
            nameof(rootNamespace));
    }

    private static bool IsEntityKey(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value[0] is >= 'a' and <= 'z'
        && value.All(character =>
            character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_')
        && !value.EndsWith('_')
        && !value.Contains("__", StringComparison.Ordinal);

    private static void Ensure(
        bool condition,
        string message,
        string parameterName)
    {
        if (!condition)
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
