using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Providers;

/// <summary>由已注册 Adapter 构成的闭合 ProviderType 目录；生产零 Adapter 时目录为空。</summary>
internal interface INotificationProviderTypeCatalog
{
    IReadOnlyList<NotificationProviderTypeDescriptor> All { get; }

    bool TryGet(string providerTypeKey, out NotificationProviderTypeDescriptor descriptor);

    bool SupportsChannel(string channelKey);
}

/// <summary>
/// 代码闭合目录。禁止反射扫描程序集或从数据库加载可执行代码。
/// </summary>
internal sealed class NotificationProviderTypeCatalog : INotificationProviderTypeCatalog
{
    private readonly IReadOnlyDictionary<string, NotificationProviderTypeDescriptor> _byKey;
    private readonly HashSet<string> _channels;

    public NotificationProviderTypeCatalog(IEnumerable<INotificationProviderAdapter> adapters)
    {
        var map = new Dictionary<string, NotificationProviderTypeDescriptor>(StringComparer.Ordinal);
        _channels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            var descriptor = adapter.Descriptor;
            if (!map.TryAdd(descriptor.ProviderTypeKey, descriptor))
            {
                throw new InvalidOperationException(
                    "Duplicate notification provider type key is not allowed.");
            }

            foreach (var channel in descriptor.SupportedChannelKeys)
            {
                _channels.Add(channel);
            }
        }

        _byKey = map;
        All = map.Values.OrderBy(item => item.ProviderTypeKey, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<NotificationProviderTypeDescriptor> All { get; }

    public bool TryGet(string providerTypeKey, out NotificationProviderTypeDescriptor descriptor) =>
        _byKey.TryGetValue(providerTypeKey, out descriptor!);

    public bool SupportsChannel(string channelKey) => _channels.Contains(channelKey);
}
