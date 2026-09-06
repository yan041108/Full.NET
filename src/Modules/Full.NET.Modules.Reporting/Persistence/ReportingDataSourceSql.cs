using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表数据源管理 SQL。</summary>
internal static class ReportingDataSourceSql
{
    private const string SelectColumns = """
        source.Id,
               source.TenantId,
               source.Name,
               source.ProviderKey,
               source.ServerHost,
               source.Port,
               source.DatabaseName,
               source.Username,
               source.PasswordProtected,
               source.TrustServerCertificate,
               source.IsEnabled,
               source.LastTestedAtUtc,
               source.LastTestStatusKey,
               source.LastTestMessage,
               source.CreatedAtUtc,
               source.UpdatedAtUtc,
               source.Version
        """;

    public static readonly SqlStatement Insert = new(
        "reporting.insert_data_source",
        """
        INSERT INTO fn_reporting_data_source
            (Id, TenantId, Name, ProviderKey, ServerHost, Port, DatabaseName, Username,
             PasswordProtected, TrustServerCertificate, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Name, @ProviderKey, @ServerHost, @Port, @DatabaseName, @Username,
             @PasswordProtected, @TrustServerCertificate, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "reporting.find_data_source_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_reporting_data_source AS source
        WHERE source.Id = @DataSourceId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "reporting.update_data_source",
        """
        UPDATE fn_reporting_data_source
        SET Name = @Name,
            ProviderKey = @ProviderKey,
            ServerHost = @ServerHost,
            Port = @Port,
            DatabaseName = @DatabaseName,
            Username = @Username,
            PasswordProtected = @PasswordProtected,
            TrustServerCertificate = @TrustServerCertificate,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DataSourceId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTestResult = new(
        "reporting.update_data_source_test_result",
        """
        UPDATE fn_reporting_data_source
        SET LastTestedAtUtc = @LastTestedAtUtc,
            LastTestStatusKey = @LastTestStatusKey,
            LastTestMessage = @LastTestMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DataSourceId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Disable = new(
        "reporting.disable_data_source",
        """
        UPDATE fn_reporting_data_source
        SET IsEnabled = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DataSourceId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Delete = new(
        "reporting.delete_data_source",
        """
        DELETE FROM fn_reporting_data_source
        WHERE Id = @DataSourceId
        """,
        SqlDataScope.HostOnly);

    public static readonly string CountSqlServer = """
        SELECT COUNT(1)
        FROM fn_reporting_data_source AS source
        WHERE (@TenantId IS NULL OR source.TenantId = @TenantId)
          AND (@NameContains IS NULL OR source.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR source.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListSqlServer = $"""
        SELECT {SelectColumns}
        FROM fn_reporting_data_source AS source
        WHERE (@TenantId IS NULL OR source.TenantId = @TenantId)
          AND (@NameContains IS NULL OR source.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR source.IsEnabled = @IsEnabled)
        ORDER BY source.Name, source.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountMySql = """
        SELECT COUNT(1)
        FROM fn_reporting_data_source AS source
        WHERE (@TenantId IS NULL OR source.TenantId = @TenantId)
          AND (@NameContains IS NULL OR source.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR source.IsEnabled = @IsEnabled)
        """;

    public static readonly string ListMySql = $"""
        SELECT {SelectColumns}
        FROM fn_reporting_data_source AS source
        WHERE (@TenantId IS NULL OR source.TenantId = @TenantId)
          AND (@NameContains IS NULL OR source.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR source.IsEnabled = @IsEnabled)
        ORDER BY source.Name, source.Id
        LIMIT @PageSize OFFSET @Offset
        """;
}
