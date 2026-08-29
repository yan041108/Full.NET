namespace Full.NET.Modules.ObservabilityAdmin.Contracts;

/// <summary>Host 日志文件控制面的精确权限码。</summary>
public static class ObservabilityLogFilePermissions
{
    public const string Read = "observability.log_files.read";

    public const string Download = "observability.log_files.download";
}

/// <summary>Host 日志控制面的稳定错误码。</summary>
public static class ObservabilityAdminErrorCodes
{
    public const string Prefix = "observability.";

    public const string LogFileNotFound = "observability.log_files.not_found";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([LogFileNotFound]);
}
