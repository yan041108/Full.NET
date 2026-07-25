using System.Resources;

using Full.NET.Hosting.Api;

using Full.NET.Modules.Files.Contracts;



namespace Full.NET.Modules.Files.Resources;



internal sealed class FilesErrorResourceSource()

    : ResourceManagerErrorResourceSource(

        FilesErrorCodes.Prefix,

        new ResourceManager(

            "Full.NET.Modules.Files.Resources.FilesErrors",

            typeof(FilesErrorResourceSource).Assembly));

