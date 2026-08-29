using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.ObservabilityAdmin.Contracts;

namespace Full.NET.Modules.ObservabilityAdmin.Resources;

internal sealed class ObservabilityAdminErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        ObservabilityAdminErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.ObservabilityAdmin.Resources.ObservabilityAdminErrors",
            typeof(ObservabilityAdminErrorResourceSource).Assembly));
