namespace Full.NET.Modules.ImportExport.Configuration;

/// <summary>ImportExport 模块上传与预校验边界配置。</summary>
public sealed class ImportExportOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:ImportExport";

    /// <summary>允许上传的最大字节数，默认 1 MiB。</summary>
    public long MaxUploadBytes { get; init; } = 1024 * 1024;

    /// <summary>单次预校验允许的最大数据行数，默认 1000。</summary>
    public int MaxPreviewRows { get; init; } = 1_000;
}
