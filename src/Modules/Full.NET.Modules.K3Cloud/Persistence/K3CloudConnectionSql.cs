using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.K3Cloud.Persistence;

/// <summary>K3Cloud 连接配置 SQL。</summary>
internal static class K3CloudConnectionSql
{
    private const string Columns = """
        Id, Name, BaseUrl, AcctId, Username, PasswordProtected, Lcid, IsDefault, IsEnabled,
        LastTestedAtUtc, LastTestStatusKey, LastTestMessage, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    public static readonly SqlStatement Insert = new(
        "k3cloud.connection.insert",
        $"""
        INSERT INTO fn_k3cloud_connection_config
            ({Columns})
        VALUES
            (@Id, @Name, @BaseUrl, @AcctId, @Username, @PasswordProtected, @Lcid, @IsDefault, @IsEnabled,
             @LastTestedAtUtc, @LastTestStatusKey, @LastTestMessage, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "k3cloud.connection.update",
        """
        UPDATE fn_k3cloud_connection_config
        SET Name = @Name,
            BaseUrl = @BaseUrl,
            AcctId = @AcctId,
            Username = @Username,
            PasswordProtected = @PasswordProtected,
            Lcid = @Lcid,
            IsDefault = @IsDefault,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "k3cloud.connection.find_by_id",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_connection_config
        WHERE Id = @ConnectionConfigId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement List = new(
        "k3cloud.connection.list",
        $"""
        SELECT {Columns}
        FROM fn_k3cloud_connection_config
        ORDER BY IsDefault DESC, Name, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTestResult = new(
        "k3cloud.connection.update_test_result",
        """
        UPDATE fn_k3cloud_connection_config
        SET LastTestedAtUtc = @LastTestedAtUtc,
            LastTestStatusKey = @LastTestStatusKey,
            LastTestMessage = @LastTestMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConnectionConfigId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClearDefaultExcept = new(
        "k3cloud.connection.clear_default_except",
        """
        UPDATE fn_k3cloud_connection_config
        SET IsDefault = 0,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE IsDefault = 1
          AND Id <> @ExceptId
        """,
        SqlDataScope.HostOnly);
}
