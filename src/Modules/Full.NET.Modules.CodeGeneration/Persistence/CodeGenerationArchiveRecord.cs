namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>不可变归档元数据随运行历史保留；到期只删除包，仍能明确区分过期与存储丢失。</summary>
internal sealed class CodeGenerationArchiveRecord
{
    public Guid Id { get; init; }
    public string SchemaJson { get; init; } = string.Empty;
    public string SchemaSha256 { get; init; } = string.Empty;
    public string GeneratorVersion { get; init; } = string.Empty;
    public string ManifestSha256 { get; init; } = string.Empty;
    public string ZipSha256 { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long ByteLength { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
