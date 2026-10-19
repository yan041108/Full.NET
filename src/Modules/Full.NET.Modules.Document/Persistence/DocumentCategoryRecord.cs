/// <summary>Document 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentCategoryRecord
{
    public Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public string? Code { get; init; }

    public string? Icon { get; init; }

    public string? Color { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public long Version { get; init; }
}
