namespace Full.NET.Modules.ObservabilityAdmin.Configuration;

/// <summary>
/// 定义 Host 日志控制面的固定根目录与有界读取限制。
/// </summary>
public sealed class ObservabilityAdminOptions
{
    public const string SectionName = "FullNet:ObservabilityAdmin";

    public string LogRootPath { get; init; } = "logs";

    public int MaximumListFiles { get; init; } = 100;

    public int DefaultTailLines { get; init; } = 200;

    public int MaximumTailLines { get; init; } = 5_000;

    public int DefaultTailBytes { get; init; } = 256 * 1024;

    public int MaximumTailBytes { get; init; } = 1024 * 1024;
}
