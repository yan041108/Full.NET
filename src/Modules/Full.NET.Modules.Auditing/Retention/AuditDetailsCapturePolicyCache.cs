using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

/// <summary>API 请求只读本地短期资格；未知、失败、未来时间和过期积压均拒绝详情。</summary>
internal sealed class AuditDetailsCapturePolicyCache(
    IClock clock,
    IOptionsMonitor<AuditDetailsCaptureOptions> options)
{
    private sealed record CachedCheckpoint(
        AuditDetailsCleanupCheckpointSnapshot Snapshot,
        DateTimeOffset ReadAtUtc);

    private CachedCheckpoint? _checkpoint;

    public bool CanCapture()
    {
        AuditDetailsCaptureOptions current;
        try
        {
            current = options.CurrentValue;
        }
        catch (Exception)
        {
            return false;
        }

        if (!current.Enabled)
        {
            return false;
        }

        var cached = Volatile.Read(ref _checkpoint);
        if (cached is null)
        {
            return false;
        }

        var nowUtc = clock.UtcNow.ToUniversalTime();
        var snapshot = cached.Snapshot;
        if (cached.ReadAtUtc > nowUtc
            || snapshot.LastSuccessfulCleanupAtUtc > nowUtc)
        {
            return false;
        }

        if (nowUtc - cached.ReadAtUtc > TimeSpan.FromSeconds(current.MaxCacheAgeSeconds)
            || nowUtc - snapshot.LastSuccessfulCleanupAtUtc >
                TimeSpan.FromSeconds(current.MaxCheckpointAgeSeconds))
        {
            return false;
        }

        var oldest = snapshot.OldestExpiredAtUtc;
        return oldest is null
            || (oldest <= snapshot.LastSuccessfulCleanupAtUtc
                && nowUtc - oldest.Value <= TimeSpan.FromSeconds(current.MaxCleanupLagSeconds));
    }

    internal void RecordSuccessfulRead(AuditDetailsCleanupCheckpointSnapshot? snapshot)
    {
        Volatile.Write(ref _checkpoint, snapshot is { } value
            ? new CachedCheckpoint(value, clock.UtcNow.ToUniversalTime())
            : null);
    }

    internal void Clear() => Volatile.Write(ref _checkpoint, null);
}
