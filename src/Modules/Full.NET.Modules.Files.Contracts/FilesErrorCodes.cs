namespace Full.NET.Modules.Files.Contracts;

public static class FilesErrorCodes
{
    public const string Prefix = "files.";
    public const string FileNotFound = "files.file.not_found";
    public const string InvalidUpload = "files.file.invalid_upload";
    public const string FileTooLarge = "files.file.too_large";
    public const string InvalidClaim = "files.file_reference_claim.invalid";
    public const string ClaimNotFound = "files.file_reference_claim.not_found";
    public const string ClaimPayloadConflict = "files.file_reference_claim.payload_conflict";
    public const string FileReferenced = "files.file.referenced";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        FileNotFound,
        InvalidUpload,
        FileTooLarge,
        InvalidClaim,
        ClaimNotFound,
        ClaimPayloadConflict,
        FileReferenced,
    ]);
}