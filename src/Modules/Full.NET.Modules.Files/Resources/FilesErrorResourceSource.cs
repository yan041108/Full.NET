using System.Resources;

using Full.NET.Hosting.Api;

using Full.NET.Modules.Files.Contracts;



/// <summary>Files 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Files.Resources;



internal sealed class FilesErrorResourceSource()

    : ResourceManagerErrorResourceSource(

        FilesErrorCodes.Prefix,

        new ResourceManager(

            "Full.NET.Modules.Files.Resources.FilesErrors",

            typeof(FilesErrorResourceSource).Assembly));

