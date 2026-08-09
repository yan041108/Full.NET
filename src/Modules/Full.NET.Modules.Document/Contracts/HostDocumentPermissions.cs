namespace Full.NET.Modules.Document.Contracts;

public static class HostDocumentPermissions
{
    public const string Read = "document.host_documents.read";

    public const string Create = "document.host_documents.create";

    public const string Update = "document.host_documents.update";

    public const string AddVersion = "document.host_documents.add_version";

    public const string Download = "document.host_documents.download";

    public const string Delete = "document.host_documents.delete";

    public const string Restore = "document.host_documents.restore";
}

public static class HostDocumentRecycleBinPermissions
{
    public const string Read = "document.host_recycle_bin.read";

    public const string Restore = "document.host_recycle_bin.restore";

    public const string Purge = "document.host_recycle_bin.purge";
}

public static class HostDocumentPermissionManagementPermissions
{
    public const string Read = "document.host_permissions.read";

    public const string Manage = "document.host_permissions.manage";
}

public static class HostDocumentSharePermissions
{
    public const string Read = "document.host_shares.read";

    public const string Create = "document.host_shares.create";

    public const string UpdateStatus = "document.host_shares.update_status";
}

public static class HostDocumentStatisticsPermissions
{
    public const string Read = "document.host_statistics.read";
}
