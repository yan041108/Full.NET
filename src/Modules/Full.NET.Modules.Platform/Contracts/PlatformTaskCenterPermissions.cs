namespace Full.NET.Modules.Platform.Contracts;

/// <summary>本人任务聚合页资格；各来源读取和操作仍使用来源模块的精确权限。</summary>
public static class PlatformTaskCenterPermissions
{
    /// <summary>允许进入当前租户的本人任务页，不隐式授予来源权限。</summary>
    public const string Read = "platform.my_tasks.read";
}
