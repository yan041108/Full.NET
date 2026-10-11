using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Configuration;

/// <summary>
/// Host 版本保留策略的数据库覆盖缓存；无行时仅使用 appsettings 绑定值。
/// </summary>
internal sealed class DocumentVersionRetentionSettingsStore
{
    private readonly object _gate = new();
    private DocumentVersionRetentionSettingRecord? _hostOverride;

    public void SetHostOverride(DocumentVersionRetentionSettingRecord? record)
    {
        lock (_gate)
        {
            // 异步重载可能逆序完成，不允许旧数据库快照回退已生效策略；无覆盖仍可恢复配置。
            if (record is not null && _hostOverride is not null && record.Version < _hostOverride.Version)
            {
                return;
            }

            _hostOverride = record;
        }
    }

    public void ApplyOverrides(DocumentVersionRetentionOptions options)
    {
        DocumentVersionRetentionSettingRecord? record;
        lock (_gate)
        {
            record = _hostOverride;
        }

        if (record is null)
        {
            return;
        }

        options.MinimumRetainedVersionsPerItem = record.MinimumRetainedVersionsPerItem;
        options.MaximumRetainedHistoryVersions = record.MaximumRetainedHistoryVersions;
        options.PollSeconds = record.PollSeconds;
        options.BatchSize = record.BatchSize;
    }
}
