using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ocr.Persistence;

internal static class OcrProviderSql
{
    private const string Columns = """
        Id, ProviderKey, Name, BaseUrl, ApiKeyProtected, IsEnabled,
        LastTestedAtUtc, LastTestStatusKey, LastTestMessage, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    public static readonly SqlStatement FindByKey = new(
        "ocr.provider.find_by_key",
        $"""
        SELECT {Columns}
        FROM fn_ocr_provider_config
        WHERE ProviderKey = @ProviderKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "ocr.provider.update",
        """
        UPDATE fn_ocr_provider_config
        SET Name = @Name,
            BaseUrl = @BaseUrl,
            ApiKeyProtected = @ApiKeyProtected,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateTestResult = new(
        "ocr.provider.update_test_result",
        """
        UPDATE fn_ocr_provider_config
        SET LastTestedAtUtc = @LastTestedAtUtc,
            LastTestStatusKey = @LastTestStatusKey,
            LastTestMessage = @LastTestMessage,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}
