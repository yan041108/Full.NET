namespace Full.NET.Modules.GoView.Contracts;

/// <summary>GoView 大屏项目权限码。</summary>
public static class GoViewProjectPermissions
{
    /// <summary>读取大屏项目与发布版本。</summary>
    public const string Read = "goview.projects.read";

    /// <summary>创建大屏项目。</summary>
    public const string Create = "goview.projects.create";

    /// <summary>更新大屏项目草稿画布。</summary>
    public const string Update = "goview.projects.update";

    /// <summary>发布大屏项目版本快照。</summary>
    public const string Publish = "goview.projects.publish";

    /// <summary>预览已发布大屏项目快照。</summary>
    public const string Preview = "goview.projects.preview";
}
