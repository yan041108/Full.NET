using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 模型配置管理 SQL。</summary>
internal static class AiModelConfigSql
{
    private const string SelectColumns = """
        config.Id,
               config.TenantId,
               config.Name,
               config.ProviderKey,
               config.EndpointBaseUrl,
               config.ModelId,
               config.ApiKeyProtected,
               config.OrganizationId,
               config.IsDefault,
               config.IsEnabled,
               config.LastTestedAtUtc,
               config.LastTestStatusKey,
               config.LastTestMessage,
               config.CreatedAtUtc,
               config.UpdatedAtUtc,
               config.Version
        """;

    public static readonly SqlStatement Insert = new(
        "ai.insert_model_config",
        """
        INSERT INTO fn_ai_model_config
            (Id, TenantId, Name, ProviderKey, EndpointBaseUrl, ModelId, ApiKeyProtected,
             OrganizationId, IsDefault, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Name, @ProviderKey, @EndpointBaseUrl, @ModelId, @ApiKeyProtected,
             @OrganizationId, @IsDefault, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "ai.find_model_config_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_ai_model_config AS config
        WHERE config.Id = @ModelConfigId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "ai.update_model_config",
        """
        UPDATE fn_ai_model_config
        SET Name = @Name,
            ProviderKey = @ProviderKey,
            EndpointBaseUrl = @EndpointBaseUrl,
            ModelId = @ModelId,
            ApiKeyProtected = @ApiKeyProtected,
            OrganizationId = @OrganizationId,
            IsDefault = @IsDefault,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ModelConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClearDefaultForScope = new(
        "ai.clear_default_model_config_for_scope",
        """
        UPDATE fn_ai_model_config
        SET IsDefault = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE ((@TenantId IS NULL AND TenantId IS NULL) OR TenantId = @TenantId)
          AND IsDefault = 1
          AND Id <> @ExcludeModelConfigId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTestResult = new(
        "ai.update_model_config_test_result",
        """
        UPDATE fn_ai_model_config
        SET LastTestedAtUtc = @LastTestedAtUtc,
            LastTestStatusKey = @LastTestStatusKey,
            LastTestMessage = @LastTestMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ModelConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Disable = new(
        "ai.disable_model_config",
        """
        UPDATE fn_ai_model_config
        SET IsEnabled = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ModelConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_ai_model_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@NameContains IS NULL OR config.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_ai_model_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@NameContains IS NULL OR config.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        ORDER BY config.Name, config.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_ai_model_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@NameContains IS NULL OR config.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_ai_model_config AS config
        WHERE (@TenantId IS NULL OR config.TenantId = @TenantId)
          AND (@NameContains IS NULL OR config.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR config.IsEnabled = @IsEnabled)
        ORDER BY config.Name, config.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
