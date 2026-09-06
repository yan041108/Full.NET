namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表数据源持久化行。</summary>
internal sealed class ReportingDataSourceRecord
{
    /// <summary>数据源标识。</summary>
    public Guid Id { get; set; }

    /// <summary>所属租户；为空表示 Host 级配置。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>数据库提供程序键。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>服务器主机名。</summary>
    public string ServerHost { get; set; } = string.Empty;

    /// <summary>端口。</summary>
    public int Port { get; set; }

    /// <summary>数据库名。</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>只读账号用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>受保护密码密文。</summary>
    public string PasswordProtected { get; set; } = string.Empty;

    /// <summary>是否信任服务器证书。</summary>
    public bool TrustServerCertificate { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>最近测试时间。</summary>
    public DateTimeOffset? LastTestedAtUtc { get; set; }

    /// <summary>最近测试状态键。</summary>
    public string? LastTestStatusKey { get; set; }

    /// <summary>最近测试摘要。</summary>
    public string? LastTestMessage { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; set; }
}
