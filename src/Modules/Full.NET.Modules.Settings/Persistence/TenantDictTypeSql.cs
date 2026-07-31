using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

internal static class TenantDictTypeSql
{
    public static readonly SqlStatement CountTenantDictTypes = new(
        "settings.count_tenant_dict_types",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_type
        WHERE TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListTenantDictTypesSqlServer = new(
        "settings.list_tenant_dict_types.sql_server",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Name, Code, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListTenantDictTypesMySql = new(
        "settings.list_tenant_dict_types.mysql",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE TenantId = @TenantId
        ORDER BY DisplayOrder, Name, Code, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindById = new(
        "settings.tenant_dict_type.find_by_id",
        """
        SELECT Id,
               Code,
               Name,
               Description,
               DisplayOrder,
               IsActive,
               CreatedAtUtc,
               UpdatedAtUtc,
               Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindIdentityById = new(
        "settings.tenant_dict_type.find_identity_by_id",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByCode = new(
        "settings.tenant_dict_type.find_by_code",
        """
        SELECT Id, Code, Name, Description, DisplayOrder, IsActive, Version
        FROM fn_settings_dict_type
        WHERE Code = @Code
          AND TenantId = @TenantId
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "settings.tenant_dict_type.insert",
        """
        INSERT INTO fn_settings_dict_type
            (Id, TenantId, Code, Name, Description, DisplayOrder, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Code, @Name, @Description, @DisplayOrder, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateTenantDictType = new(
        "settings.tenant_dict_type.update",
        """
        UPDATE fn_settings_dict_type
        SET Name = @Name,
            Description = @Description,
            DisplayOrder = @DisplayOrder,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DisableTenantDictType = new(
        "settings.tenant_dict_type.disable",
        """
        UPDATE fn_settings_dict_type
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DictTypeId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountActiveItems = new(
        "settings.tenant_dict_type.count_active_items",
        """
        SELECT COUNT(1)
        FROM fn_settings_dict_item item
        INNER JOIN fn_settings_dict_type type ON type.Id = item.DictTypeId
        WHERE item.DictTypeId = @DictTypeId
          AND type.TenantId = @TenantId
          AND item.IsActive = 1
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
