using Full.NET.Composition;
using Full.NET.Modules.Document;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Configuration;
using System.Text;

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
    public void Unknown_preset_must_not_enable_all_modules()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
            {
                ["FullNet:Modules:Preset"] = "Typo",
            })));

        StringAssert.Contains(exception.Message, "Preset");
    }

    [TestMethod]
    public void Explicit_empty_enabled_array_must_not_enable_all_modules()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"FullNet":{"Modules":{"Enabled":[]}}}"""));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            FullNetModuleSelection.ResolveEnabledNames(configuration));
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
    public void Saas_preset_enables_payments_and_webhooks()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Saas,
        }));

        CollectionAssert.AreEquivalent(
            FullNetModuleSelection.SaasPresetModuleNames.ToArray(),
            enabled.ToArray());
        Assert.IsTrue(enabled.Contains("Payments"));
        Assert.IsTrue(enabled.Contains("Webhooks"));
    }

    [TestMethod]
    public void Enterprise_preset_enables_workflow_delivery_and_sample_module()
    {
        var enabled = FullNetModuleSelection.ResolveEnabledNames(CreateConfiguration(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Enterprise,
        }));

        CollectionAssert.AreEquivalent(
            FullNetModuleSelection.EnterprisePresetModuleNames.ToArray(),
            enabled.ToArray());
        Assert.IsTrue(enabled.Contains("EnterpriseRequest"));
        Assert.IsTrue(enabled.Contains("Workflow"));
        Assert.IsTrue(enabled.Contains("ImportExport"));
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
                ["FullNet:Modules:Enabled:4"] = "Files",
            }),
            [new IdentityModule(), new TenancyModule(), new OrganizationModule(), new Full.NET.Modules.Files.FilesModule(), new NotificationsModule()]);

        CollectionAssert.AreEquivalent(
            new[] { "Identity", "Tenancy", "Organization", "Files", "Notifications" },
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
