namespace Full.NET.IntegrationTests.Files;

/// <summary>Host 文件 SQL 测试参数；MySQL 要求可空参数显式出现在字典中。</summary>
internal static class HostFileSqlTestParameters
{
    public static IReadOnlyDictionary<string, object?> Insert(
        Guid id,
        Guid? folderId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string providerKey,
        string storageKey,
        string? contentHash,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId) =>
        new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["FolderId"] = folderId,
            ["OriginalFileName"] = originalFileName,
            ["ContentType"] = contentType,
            ["SizeBytes"] = sizeBytes,
            ["ProviderKey"] = providerKey,
            ["StorageKey"] = storageKey,
            ["ContentHash"] = contentHash,
            ["CreatedAtUtc"] = createdAtUtc,
            ["CreatedByUserId"] = createdByUserId,
        };
}
