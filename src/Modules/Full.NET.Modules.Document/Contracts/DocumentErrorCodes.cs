namespace Full.NET.Modules.Document.Contracts;

public static class DocumentErrorCodes
{
    public const string Prefix = "document.";
    public const string Invalid = "document.host_document.invalid";
    public const string NotFound = "document.host_document.not_found";
    public const string VersionConflict = "document.host_document.version_conflict";
    public const string InvalidFileReference = "document.host_document.invalid_file_reference";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Invalid,
        NotFound,
        VersionConflict,
        InvalidFileReference,
    ]);
}
