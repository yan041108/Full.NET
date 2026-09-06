using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;

/// <summary>汇总实例目录与当前进程运行时快照，不暴露环境变量或连接串。</summary>
internal sealed class ServerMonitorService
{
    private readonly ObservabilityAdminOptions _options;
    private readonly ServerRuntimeReader _runtimeReader;

    /// <summary>创建服务器监控服务。</summary>
    /// <param name="options">可观测性管理配置。</param>
    /// <param name="runtimeReader">当前进程运行时读取器。</param>
    public ServerMonitorService(
        IOptions<ObservabilityAdminOptions> options,
        ServerRuntimeReader runtimeReader)
    {
        _options = options.Value;
        _runtimeReader = runtimeReader;
    }

    /// <summary>返回实例目录，并确保当前进程实例始终可见。</summary>
    public IReadOnlyList<ServerInstanceCatalogEntry> ListInstances()
    {
        var currentKey = ResolveCurrentInstanceKey();
        var entries = new Dictionary<string, ServerInstanceCatalogEntry>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var configured in _options.Instances)
        {
            if (string.IsNullOrWhiteSpace(configured.InstanceKey))
            {
                continue;
            }

            var key = configured.InstanceKey.Trim();
            entries[key] = new ServerInstanceCatalogEntry(
                key,
                ResolveDisplayName(configured.DisplayName, key),
                ResolveHostRole(configured.HostRole),
                string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase),
                string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase)
                    ? ServerInstanceRuntimeQueryability.Local
                    : ServerInstanceRuntimeQueryability.CatalogOnly);
        }

        entries[currentKey] = new ServerInstanceCatalogEntry(
            currentKey,
            ResolveDisplayName(_options.InstanceDisplayName, currentKey),
            ResolveHostRole(_options.HostRole),
            true,
            ServerInstanceRuntimeQueryability.Local);

        return entries.Values
            .OrderByDescending(entry => entry.IsCurrent)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>读取指定实例的运行时快照；仅当前进程实例可查询。</summary>
    /// <param name="instanceKey">实例稳定标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行时快照；实例不存在或不可本地查询时返回 <see langword="null"/>。</returns>
    public async Task<ServerRuntimeSnapshot?> GetRuntimeAsync(
        string instanceKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceKey))
        {
            return null;
        }

        var normalizedKey = instanceKey.Trim();
        var catalog = ListInstances();
        var entry = catalog.FirstOrDefault(
            candidate => string.Equals(
                candidate.InstanceKey,
                normalizedKey,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null
            || !string.Equals(
                entry.RuntimeQueryability,
                ServerInstanceRuntimeQueryability.Local,
                StringComparison.Ordinal))
        {
            return null;
        }

        return await _runtimeReader.CaptureAsync(
                entry.InstanceKey,
                entry.DisplayName,
                entry.HostRole,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string ResolveCurrentInstanceKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.InstanceKey))
        {
            return _options.InstanceKey.Trim();
        }

        var role = ResolveHostRole(_options.HostRole);
        return $"{Environment.MachineName}-{role}";
    }

    private static string ResolveDisplayName(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    private static string ResolveHostRole(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? "Api" : configured.Trim();
}
