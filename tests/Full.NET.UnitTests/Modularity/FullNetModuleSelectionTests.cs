using Full.NET.Composition;
using Full.NET.Modules.Document;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class FullNetModuleSelectionTests
{
    [TestMethod]
    public void Default_configuration_enables_all_official_modules()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration());

        Assert.HasCount(FullNetModuleSelection.OfficialModuleNames.Count, enabled);
        foreach (var name in FullNetModuleSelection.OfficialModuleNames)
        {
            Assert.IsTrue(enabled.Contains(name));
        }
    }

    [TestMethod]
    public void Minimal_preset_enables_core_platform_modules_only()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Minimal,
        }));

        CollectionAssert.AreEquivalent(
            FullNetModuleSelection.MinimalPresetModuleNames.ToArray(),
            enabled.ToArray());
    }

    [TestMethod]
    public void Platform_preset_enables_platform_modules()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Platform,
        }));

        CollectionAssert.AreEquivalent(
            FullNetModuleSelection.PlatformPresetModuleNames.ToArray(),
            enabled.ToArray());
    }

    [TestMethod]
    public void Content_preset_enables_content_modules()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Content,
        }));

        CollectionAssert.AreEquivalent(
            FullNetModuleSelection.ContentPresetModuleNames.ToArray(),
            enabled.ToArray());
    }

    [TestMethod]
    public void Explicit_enabled_list_must_include_module_dependencies()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            FullNetModuleSelection.ResolveEnabledModules(
                CreateConfiguration(new Dictionary<string, string?>
                {
                    ["FullNet:Modules:Enabled:0"] = "Identity",
                    ["FullNet:Modules:Enabled:1"] = "Document",
                }),
                [new IdentityModule(), new DocumentModule()]));

        StringAssert.Contains(exception.Message, "Files");
    }

    [TestMethod]
    public void Explicit_enabled_list_may_omit_optional_event_producer_module()
    {
        var modules = FullNetModuleSelection.ResolveEnabledModules(
            CreateConfiguration(new Dictionary<string, string?>
            {
                ["FullNet:Modules:Enabled:0"] = "Identity",
                ["FullNet:Modules:Enabled:1"] = "Tenancy",
                ["FullNet:Modules:Enabled:2"] = "Organization",
                ["FullNet:Modules:Enabled:3"] = "Notifications",
            }),
            [new IdentityModule(), new TenancyModule(), new OrganizationModule(), new NotificationsModule()]);

        CollectionAssert.AreEquivalent(
            new[] { "Identity", "Tenancy", "Organization", "Notifications" },
            modules.Select(module => module.Name).ToArray());
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values is not null)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder.Build();
    }
}
