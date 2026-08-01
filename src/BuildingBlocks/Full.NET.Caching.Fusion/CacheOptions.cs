namespace Full.NET.Caching.Fusion;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan Jitter { get; set; } = TimeSpan.FromSeconds(30);

    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 按条目名注册的缓存策略。默认已内置 <c>tenancy.tenant-resolution</c>=S1；
    /// 此处同名配置可覆盖，但不得为全部业务强塞同一 TTL。
    /// </summary>
    public Dictionary<string, CacheEntryDefinitionOptions> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
