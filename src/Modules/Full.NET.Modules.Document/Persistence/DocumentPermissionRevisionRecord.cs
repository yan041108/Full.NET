namespace Full.NET.Modules.Document.Persistence;

/// <summary>授权快照只读取父文档标识和修订号，原生物化不得依赖完整文档的其他列。</summary>
internal sealed class DocumentPermissionRevisionRecord
{
    public Guid Id { get; init; }

    public long Version { get; init; }
}
