namespace Full.NET.Modules.Document.Contracts;

public static class DocumentErrorCodes
{
    public const string Prefix = "document.";
    public const string Invalid = "document.host_document.invalid";
    public const string NotFound = "document.host_document.not_found";
    public const string VersionConflict = "document.host_document.version_conflict";
    public const string InvalidFileReference = "document.host_document.invalid_file_reference";
    public const string CategoryInvalid = "document.host_category.invalid";
    public const string CategoryNotFound = "document.host_category.not_found";
    public const string CategoryNameExists = "document.host_category.name_exists";
    public const string CategoryInvalidParent = "document.host_category.invalid_parent";
    public const string CategoryVersionConflict = "document.host_category.version_conflict";
    public const string CategoryHasChildren = "document.host_category.has_children";
    public const string CategoryInUse = "document.host_category.in_use";
    public const string TagInvalid = "document.host_tag.invalid";
    public const string TagNotFound = "document.host_tag.not_found";
    public const string TagNameExists = "document.host_tag.name_exists";
    public const string TagVersionConflict = "document.host_tag.version_conflict";
    public const string TagInUse = "document.host_tag.in_use";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Invalid,
        NotFound,
        VersionConflict,
        InvalidFileReference,
        CategoryInvalid,
        CategoryNotFound,
        CategoryNameExists,
        CategoryInvalidParent,
        CategoryVersionConflict,
        CategoryHasChildren,
        CategoryInUse,
        TagInvalid,
        TagNotFound,
        TagNameExists,
        TagVersionConflict,
        TagInUse,
    ]);
}
