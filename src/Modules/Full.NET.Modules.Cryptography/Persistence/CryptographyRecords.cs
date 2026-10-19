/// <summary>Cryptography 持久化相关类型；仅供 Dapper 或内部编排使用。</summary>
namespace Full.NET.Modules.Cryptography.Persistence;

internal sealed class CryptographyKeyRecord
{
    public Guid Id { get; init; }

    public string KeyKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Algorithm { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string PublicKeyHex { get; init; } = string.Empty;

    public string PublicKeyFingerprint { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
