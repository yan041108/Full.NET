namespace Full.NET.Hosting.Forwarding;

/// <summary>
/// 定义 API 接受转发请求信息时允许信任的代理边界。
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxy";

    /// <summary>
    /// 是否启用可信代理转发；默认关闭，避免未配置部署隐式信任请求 Header。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 单个请求最多处理的受信代理层数。
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// API 连接层能够直接看到的可信代理 IP 地址。
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// API 连接层能够直接看到的可信代理 CIDR 网络。
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];
}
