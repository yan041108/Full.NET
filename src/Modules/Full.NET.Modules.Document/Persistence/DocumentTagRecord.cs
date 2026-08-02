namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentTagRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public long Version { get; init; }
}
