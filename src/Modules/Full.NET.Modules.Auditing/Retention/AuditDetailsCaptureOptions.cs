using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

/// <summary>受限详情采集资格；默认关闭，清理检查点只能收缩许可。</summary>
internal sealed class AuditDetailsCaptureOptions
{
    public const string SectionName = "Auditing:DetailsCapture";

    public bool Enabled { get; set; }

    public int RefreshSeconds { get; set; } = 30;

    public int MaxCacheAgeSeconds { get; set; } = 90;

    public int MaxCheckpointAgeSeconds { get; set; } = 180;

    public int MaxCleanupLagSeconds { get; set; } = 300;

    /// <summary>详情首次捕获后的绝对保留小时数；不能超过操作摘要保留期。</summary>
    public int RetentionHours { get; set; } = 24;

    /// <summary>受控请求投影的最大 UTF-8 字节数；零表示关闭。</summary>
    public int MaxRequestPayloadBytes { get; set; } = 2_048;

    /// <summary>受控结果投影的最大 UTF-8 字节数；默认关闭。</summary>
    public int MaxResponsePayloadBytes { get; set; }

    /// <summary>允许采集受限详情的完整静态 Endpoint 模板，默认空集合。</summary>
    public string[] CaptureRouteAllowList { get; set; } = [];
}

internal sealed class AuditDetailsCaptureOptionsValidator
    : IValidateOptions<AuditDetailsCaptureOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditDetailsCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.RefreshSeconds is < 5 or > 300)
        {
            failures.Add("Auditing:DetailsCapture:RefreshSeconds must be between 5 and 300.");
        }

        if (options.MaxCacheAgeSeconds is < 10 or > 600)
        {
            failures.Add("Auditing:DetailsCapture:MaxCacheAgeSeconds must be between 10 and 600.");
        }

        if (options.MaxCheckpointAgeSeconds is < 30 or > 3600)
        {
            failures.Add("Auditing:DetailsCapture:MaxCheckpointAgeSeconds must be between 30 and 3600.");
        }

        if (options.MaxCleanupLagSeconds is < 60 or > 86400)
        {
            failures.Add("Auditing:DetailsCapture:MaxCleanupLagSeconds must be between 60 and 86400.");
        }

        if (options.RetentionHours is < 1 or > 720)
        {
            failures.Add("Auditing:DetailsCapture:RetentionHours must be between 1 and 720.");
        }

        if (options.MaxRequestPayloadBytes is < 0 or > 2_048
            || options.MaxResponsePayloadBytes is < 0 or > 2_048)
        {
            failures.Add("Auditing:DetailsCapture:Payload byte limits must be between 0 and 2048.");
        }

        if (options.CaptureRouteAllowList is null
            || options.CaptureRouteAllowList.Length > 64
            || options.CaptureRouteAllowList.Any(route =>
                string.IsNullOrWhiteSpace(route)
                || route.Length > 512
                || !route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                || route.Contains('*', StringComparison.Ordinal)
                || route.Contains('?', StringComparison.Ordinal)
                || route.Contains('#', StringComparison.Ordinal)))
        {
            failures.Add("Auditing:DetailsCapture:CaptureRouteAllowList must contain at most 64 exact API route templates.");
        }

        if (options.RefreshSeconds is >= 5 and <= 300
            && options.MaxCacheAgeSeconds is >= 10 and <= 600
            && options.RefreshSeconds >= options.MaxCacheAgeSeconds)
        {
            failures.Add("Auditing:DetailsCapture:MaxCacheAgeSeconds must exceed RefreshSeconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
