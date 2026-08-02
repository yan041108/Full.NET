using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.Modules.Document.Resources;

internal sealed class DocumentErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        DocumentErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Document.Resources.DocumentErrors",
            typeof(DocumentErrorResourceSource).Assembly));
