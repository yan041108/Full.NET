namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentCategoryRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public long Version { get; init; }
}
