namespace Full.NET.Modules.Files.Contracts;

public static class FilesErrorCodes
{
    public const string Prefix = "files.";
    public const string FileNotFound = "files.file.not_found";
    public const string InvalidUpload = "files.file.invalid_upload";
    public const string FileTooLarge = "files.file.too_large";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly([FileNotFound, InvalidUpload, FileTooLarge]);
}
