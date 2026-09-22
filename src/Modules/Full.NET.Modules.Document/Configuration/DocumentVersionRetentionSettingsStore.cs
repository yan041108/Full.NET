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
