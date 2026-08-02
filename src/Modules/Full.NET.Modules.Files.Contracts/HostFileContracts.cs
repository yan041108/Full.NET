namespace Full.NET.Modules.Files.Contracts;

/// <summary>Host 作用域文件元数据 API 的权限与契约。</summary>
public static class HostFilePermissions
{
    public const string Read = "files.files.read";
    public const string Upload = "files.files.upload";
    public const string Download = "files.files.download";
    public const string Delete = "files.files.delete";
}

public sealed record HostFileResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);
