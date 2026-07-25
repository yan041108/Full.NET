using Full.NET.Modules.Files.Contracts;

using Full.NET.Modules.Identity.Contracts;



namespace Full.NET.Modules.Files;



internal sealed class FilesAuthorizationContributor

    : IAuthorizationCatalogContributor

{

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =

    [

        new PermissionDefinition(

            HostFilePermissions.Read,

            "查询文件",

            AuthorizationScope.Host),

        new PermissionDefinition(

            HostFilePermissions.Write,

            "管理文件",

            AuthorizationScope.Host),

    ];



    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =

    [

        new NavigationDefinition(

            "host-files",

            null,

            "host-files",

            "/files/host-files",

            "host-files",

            "文件管理",

            "Files",

            "folder",

            70,

            HostFilePermissions.Read),

    ];

}

