using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Resources;

internal sealed class TenancyErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        TenancyErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Tenancy.Resources.TenancyErrors",
            typeof(TenancyErrorResourceSource).Assembly));
