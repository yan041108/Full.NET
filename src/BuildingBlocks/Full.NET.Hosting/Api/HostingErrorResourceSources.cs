using System.Resources;
using Full.NET.Abstractions.Results;
using Full.NET.Localization;

namespace Full.NET.Hosting.Api;

internal sealed class CommonErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        CommonErrorCodes.Prefix,
        CreateResourceManager())
{
    internal static ResourceManager CreateResourceManager() => new(
        "Full.NET.Hosting.Resources.CommonErrors",
        typeof(CommonErrorResourceSource).Assembly);
}

internal sealed class AuthorizationErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        CommonErrorCodes.AuthorizationPrefix,
        CommonErrorResourceSource.CreateResourceManager());

internal sealed class ValidationErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        ValidationErrorCodes.Prefix,
        CommonErrorResourceSource.CreateResourceManager());

internal sealed class LocalizationErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        LocalizationErrorCodes.Prefix,
        CommonErrorResourceSource.CreateResourceManager());
