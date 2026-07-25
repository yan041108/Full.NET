namespace Full.NET.Modules.Files.Contracts;



/// <summary>Files 模块对外返回的稳定错误码。</summary>

public static class FilesErrorCodes

{

    /// <summary>Files 错误码前缀。</summary>

    public const string Prefix = "files.";



    /// <summary>目标文件不存在或已删除。</summary>

    public const string FileNotFound = "files.file.not_found";



    /// <summary>上传内容无效。</summary>

    public const string InvalidUpload = "files.file.invalid_upload";



    /// <summary>文件超过允许大小。</summary>

    public const string FileTooLarge = "files.file.too_large";



    /// <summary>获取当前目录中的全部稳定错误码。</summary>

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(

    [

        FileNotFound,

        InvalidUpload,

        FileTooLarge,

    ]);

}

