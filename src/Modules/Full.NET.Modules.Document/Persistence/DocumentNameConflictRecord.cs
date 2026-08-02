namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentNameConflictRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public long Version { get; init; }
}
