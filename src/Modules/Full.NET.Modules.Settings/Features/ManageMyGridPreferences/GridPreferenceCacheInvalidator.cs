using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Settings.Features.ManageMyGridPreferences;

/// <summary>Settings 根据唯一可信目录构造偏好键，防止管理消费者复制 Schema 版本后失效错位。</summary>
internal sealed class GridPreferenceCacheInvalidator(ICacheInvalidator cache, IHostEnvironment environment)
    : IGridPreferenceCacheInvalidator
{
    /// <inheritdoc />
    public async ValueTask<bool> InvalidateAsync(Guid userId, string gridKey, bool includeSharedCache,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(gridKey)
            || !GridPreferenceCatalog.TryGet(gridKey, out var definition))
            return false;

        await cache.RemoveAsync(CacheEntryNames.GridPreference,
            BuildCacheKey(environment.EnvironmentName, userId, definition),
            includeSharedCache ? CacheInvalidationScope.AllLayersSynchronous : CacheInvalidationScope.CurrentNodeOnly,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>读取、提交后清理与管理入口共用键协议；版本只取自 Settings 可信目录。</summary>
    internal static string BuildCacheKey(string environmentName, Guid userId, GridPreferenceDefinition definition) =>
        CacheKeyBuilder.ForGlobal(environmentName, "settings", "grid-preference",
            $"{userId:N}:{definition.GridKey}", $"v{definition.SchemaVersion}");
}
