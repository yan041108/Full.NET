namespace Full.NET.Caching.Fusion;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan Jitter { get; set; } = TimeSpan.FromSeconds(30);

    public string? RedisConnectionString { get; set; }
}
