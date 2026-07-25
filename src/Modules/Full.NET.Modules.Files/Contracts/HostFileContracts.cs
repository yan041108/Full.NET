namespace Full.NET.Modules.Files.Contracts;



/// <summary>Host 作用域文件元数据 API 的权限与契约。</summary>

public static class HostFilePermissions

{

    /// <summary>分页查询文件元数据与下载内容。</summary>

    public const string Read = "files.files.read";



    /// <summary>上传与删除文件。</summary>

    public const string Write = "files.files.write";

}



/// <summary>Host 文件元数据列表项与详情响应。</summary>

public sealed record HostFileResponse(

    Guid Id,

    string OriginalFileName,

    string ContentType,

    long SizeBytes,

    string? ContentHash,

    DateTimeOffset CreatedAtUtc,

    Guid CreatedByUserId);


