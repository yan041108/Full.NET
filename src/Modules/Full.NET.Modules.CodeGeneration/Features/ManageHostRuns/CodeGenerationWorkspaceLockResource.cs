namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 为数据库会话锁生成稳定、可复现的工作区资源名。
/// </summary>
internal static class CodeGenerationWorkspaceLockResource
{
    private const string Prefix = "fn:codegeneration:workspace:";

    /// <summary>
    /// 基于共享卷持久化身份生成资源名，相同卷在不同实例的挂载路径不会改变协调边界。
    /// </summary>
    /// <param name="workspaceId">取得文件租约后读取的持久化工作区身份。</param>
    public static string Create(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException(
                "工作区身份不能为空。",
                nameof(workspaceId));
        }

        return Prefix + workspaceId.ToString("N");
    }
}
