namespace Full.NET.Modules.ImportExport.Configuration;

/// <summary>ImportExport 模块上传、预校验与批量执行边界配置。</summary>
public sealed class ImportExportOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:ImportExport";

    /// <summary>允许上传的最大字节数，默认 1 MiB。</summary>
    public long MaxUploadBytes { get; init; } = 1024 * 1024;

    /// <summary>单次预校验允许的最大数据行数，默认 1000。</summary>
    public int MaxPreviewRows { get; init; } = 1_000;

    /// <summary>是否启用后台批量执行 worker。</summary>
    public bool ExecutionEnabled { get; init; }

    /// <summary>后台 worker 轮询间隔秒数，默认 15 秒。</summary>
    public int PollSeconds { get; init; } = 15;

    /// <summary>单次 worker 迭代处理的 valid 行数上限，默认 50。</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>测试专用：排队后立即同步执行，不依赖后台 worker。</summary>
    public bool RunSynchronously { get; init; }
}
