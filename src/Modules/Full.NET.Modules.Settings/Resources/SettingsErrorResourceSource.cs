using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Resources;

internal sealed class SettingsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        SettingsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Settings.Resources.SettingsErrors",
            typeof(SettingsErrorResourceSource).Assembly));
