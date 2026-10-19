/// <summary>Document 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentNameConflictRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public long Version { get; init; }
}
