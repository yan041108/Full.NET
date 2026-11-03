namespace Full.NET.Modules.Settings.Persistence;

/// <summary>完整配置仅在详情或应用默认时读取，不进入列表查询。</summary>
internal sealed class QuerySchemeRecord
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public int SchemaVersion { get; set; }
    public bool IsShared { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public string? ConfigurationJson { get; set; }
}

/// <summary>当前用户、作用域及 Grid 的写入互斥和默认选择记录。</summary>
internal sealed class QuerySchemeDefaultRecord
{
    public Guid? DefaultSchemeId { get; set; }
    public long Version { get; set; }
}
