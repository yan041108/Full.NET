using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Resources;

internal sealed class IdentityErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        IdentityErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Identity.Resources.IdentityErrors",
            typeof(IdentityErrorResourceSource).Assembly));
