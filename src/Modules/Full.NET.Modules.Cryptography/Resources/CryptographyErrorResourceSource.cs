using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Cryptography.Contracts;

namespace Full.NET.Modules.Cryptography.Resources;

internal sealed class CryptographyErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        CryptographyErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Cryptography.Resources.CryptographyErrors",
            typeof(CryptographyErrorResourceSource).Assembly));
