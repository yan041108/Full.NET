namespace Full.NET.Modules.Document.Contracts;

public static class DocumentErrorCodes
{
    public const string Prefix = "document.";
    public const string Invalid = "document.host_document.invalid";
    public const string NotFound = "document.host_document.not_found";
    public const string VersionConflict = "document.host_document.version_conflict";
    public const string InvalidFileReference = "document.host_document.invalid_file_reference";
    public const string NoCurrentVersion = "document.host_document.no_current_version";
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
    public const string ShareNotFound = "document.share.not_found";
    public const string ShareCodeExists = "document.share.code_exists";
    public const string ShareInvalid = "document.share.invalid";
    public const string PermissionInvalid = "document.permission.invalid";
    public const string RecycleItemNotFound = "document.recycle.not_found";
    public const string RecyclePurgeFailed = "document.host_recycle_bin.purge_failed";
    public const string ShareVersionConflict = "document.host_share.version_conflict";
    public const string ShareCodeNotFound = "document.host_share.code_not_found";
    public const string ShareExpired = "document.host_share.expired";
    public const string ShareDisabled = "document.host_share.disabled";
    public const string ShareMaxAccessReached = "document.host_share.max_access_reached";
    public const string SharePasswordRequired = "document.host_share.password_required";
    public const string SharePasswordNotSupportedYet = "document.host.share.password_not_supported_yet";
    public const string SharePasswordInvalidLength = "document.host_share.password_invalid_length";
    public const string HostShareAccessDenied = "document.host_share.access_denied";
    public const string HostSharePasswordRequired = SharePasswordRequired;
    public const string PermissionDocumentNotFound = "document.host_permission.document_not_found";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        Invalid,
        NotFound,
        VersionConflict,
        InvalidFileReference,
        NoCurrentVersion,
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
        ShareNotFound,
        ShareCodeExists,
        ShareInvalid,
        PermissionInvalid,
        RecycleItemNotFound,
        RecyclePurgeFailed,
        ShareVersionConflict,
        ShareCodeNotFound,
        ShareExpired,
        ShareDisabled,
        ShareMaxAccessReached,
        SharePasswordRequired,
        SharePasswordNotSupportedYet,
        SharePasswordInvalidLength,
        HostShareAccessDenied,
        PermissionDocumentNotFound,
    ]);
}
