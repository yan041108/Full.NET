namespace Full.NET.Modules.Files.Storage;

/// <summary>本地磁盘文件存储配置；RootPath 由宿主配置提供。</summary>
public sealed class LocalFileStorageOptions
{
    public const string SectionName = "Files:Local";

    /// <summary>文件落盘根目录。</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>单次上传允许的最大字节数，默认 10 MiB。</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
