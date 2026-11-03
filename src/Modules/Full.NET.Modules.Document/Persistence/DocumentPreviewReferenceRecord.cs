namespace Full.NET.Modules.Document.Persistence;
/// <summary>上传前持久化的独立引用身份；释放后仍保留租约截止保护。</summary>
internal sealed class DocumentPreviewReferenceRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid TaskId { get; init; }
    public Guid FileId { get; init; }
    public string Purpose { get; init; } = "";
    public string StateKey { get; init; } = "";
    public DateTimeOffset? ReleaseAfterUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
