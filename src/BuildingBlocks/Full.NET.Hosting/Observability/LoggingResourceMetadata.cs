using System.Runtime.InteropServices;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 保存单个进程启动时确定的日志资源信息；普通事件只携带 Instance 作为关联键。
/// </summary>
internal sealed record LoggingResourceMetadata(
    string Application,
    string Instance,
    string HostRole,
    string Environment,
    string RuntimeVersion,
    string OSDescription,
    string FrameworkVersion)
{
    public static LoggingResourceMetadata Create(
        string application,
        string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var role = application switch
        {
            "Full.NET.Host.Api" => "Api",
            "Full.NET.Host.Worker" => "Worker",
            "Full.NET.Host.Migrator" => "Migrator",
            _ => "Other",
        };

        return new LoggingResourceMetadata(
            application,
            Guid.CreateVersion7().ToString("D"),
            role,
            environment,
            System.Environment.Version.ToString(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription);
    }
}
