namespace Full.NET.Realtime;

/// <summary>
/// 实时分组命名约定；须与连接加入逻辑保持一致。
/// </summary>
public static class RealtimeGroups
{
    /// <summary>用户私有组：接收个人通知与未读数。</summary>
    public static string User(Guid userId) => $"user:{userId:D}";

    /// <summary>租户广播组：仅当连接已验证租户上下文时加入。</summary>
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId:D}";

    /// <summary>Host 广播组：Host 作用域连接接收平台公告推送。</summary>
    public static string HostBroadcast => "host:broadcast";
}
