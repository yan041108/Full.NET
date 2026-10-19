using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;

/// <summary>Document 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Document.Resources;

internal sealed class DocumentErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        DocumentErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Document.Resources.DocumentErrors",
            typeof(DocumentErrorResourceSource).Assembly));
