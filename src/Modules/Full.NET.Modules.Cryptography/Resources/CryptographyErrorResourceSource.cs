using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Cryptography.Contracts;

/// <summary>Cryptography 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Cryptography.Resources;

internal sealed class CryptographyErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        CryptographyErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Cryptography.Resources.CryptographyErrors",
            typeof(CryptographyErrorResourceSource).Assembly));
