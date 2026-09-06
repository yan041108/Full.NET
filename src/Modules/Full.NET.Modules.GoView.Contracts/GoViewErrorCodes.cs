namespace Full.NET.Modules.GoView.Contracts;

/// <summary>GoView 模块稳定业务错误码。</summary>
public static class GoViewErrorCodes
{
    /// <summary>大屏项目不存在。</summary>
    public const string ProjectNotFound = "goview.project.not_found";

    /// <summary>大屏项目元数据或画布校验失败。</summary>
    public const string ProjectInvalid = "goview.project.invalid";

    /// <summary>大屏项目键冲突。</summary>
    public const string ProjectKeyConflict = "goview.project.key_conflict";

    /// <summary>大屏项目并发版本冲突。</summary>
    public const string ProjectConcurrencyConflict = "goview.project.concurrency_conflict";

    /// <summary>大屏项目版本不存在。</summary>
    public const string ProjectVersionNotFound = "goview.project_version.not_found";

    /// <summary>大屏项目尚未发布，无法预览。</summary>
    public const string ProjectNotPublished = "goview.project.not_published";

    /// <summary>画布 JSON 无效或超过大小上限。</summary>
    public const string CanvasJsonInvalid = "goview.canvas_json.invalid";
}
