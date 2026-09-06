namespace Full.NET.Modules.Platform.Contracts;

/// <summary>
/// Platform 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class PlatformErrorCodes
{
    /// <summary>Platform 错误码前缀。</summary>
    public const string Prefix = "platform.";

    /// <summary>更新日志未找到。</summary>
    public const string ReleaseNoteNotFound = "platform.release_note_not_found";

    /// <summary>更新日志字段校验失败。</summary>
    public const string ReleaseNoteValidationFailed = "platform.release_note_validation_failed";

    /// <summary>更新日志乐观版本号不符，并发更新冲突。</summary>
    public const string ReleaseNoteConcurrencyConflict = "platform.release_note_concurrency_conflict";

    /// <summary>更新日志当前状态不允许请求的操作。</summary>
    public const string ReleaseNoteInvalidStatus = "platform.release_note_invalid_status";

    /// <summary>版本标签已被其他更新日志占用。</summary>
    public const string ReleaseNoteVersionLabelConflict = "platform.release_note_version_label_conflict";

    /// <summary>版本标签格式无效或超出允许范围。</summary>
    public const string ReleaseNoteInvalidVersionLabel = "platform.release_note_invalid_version_label";
}
