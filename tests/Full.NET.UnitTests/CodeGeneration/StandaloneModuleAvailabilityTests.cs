using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Composition;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class StandaloneModuleAvailabilityTests
{
    private Dictionary<string, string?> environment = [];

    [TestInitialize]
    public void Isolate_module_environment()
    {
        environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key.ToString()!.Replace("__", ":").StartsWith("FullNet:Modules", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString());
        foreach (var key in environment.Keys) Environment.SetEnvironmentVariable(key, null);
    }

    [TestCleanup]
    public void Restore_module_environment()
    {
        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key.ToString()!.Replace("__", ":").StartsWith("FullNet:Modules", StringComparison.OrdinalIgnoreCase)).ToArray())
            Environment.SetEnvironmentVariable(entry.Key.ToString()!, null);
        foreach (var entry in environment) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Minimal\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"mINImAL\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Platform\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Content\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Saas\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Enterprise\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Full\"}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\"]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Tenancy\",\"Organization\",\"Settings\"]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Payments\"]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Files\"]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Organization\"]}")]
    [DataRow("{\"FULLNET:MODULES:ENABLED:first\":\"Identity\",\"FullNet:Modules:Enabled:other\":\"Tenancy\"}")]
    [DataRow("{\"FullNet:Modules:Enabled\":\"credential-probe\",\"FullNet:Modules:Enabled:0\":\"Identity\"}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",{\"Probe\":\"credential-probe\"}]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":null}")]
    [DataRow("{\"FullNet:Modules:Enabled\":\"credential-probe\"}")]
    public async Task Effective_selection_is_checked_against_the_frozen_installation_scope(string overlay)
    {
        using var fixture = new Workspace();
        fixture.WriteProfile("production", overlay);
        await AssertDiagnosis(fixture, "production", ExpectedAvailability(fixture.Configuration, overlay));
    }

    [TestMethod]
    [DataRow("profile", "development", true)]
    [DataRow("profile", "development", false)]
    [DataRow("secrets", "development", true)]
    [DataRow("secrets", "development", false)]
    [DataRow("environment", "development", true)]
    [DataRow("environment", "development", false)]
    [DataRow("profile", "production", false)]
    [DataRow("environment", "production", true)]
    public async Task Higher_sources_merge_enabled_children_before_installation_scope_validation(string source, string profile, bool valid)
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Tenancy\"]}");
        var module = valid ? "Tenancy" : "Payments";
        var overlay = JsonSerializer.Serialize(new Dictionary<string, string> { ["FULLNET:MODULES:ENABLED:1"] = module });
        var expected = ExpectedAvailability(fixture.Configuration, overlay);
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment") Environment.SetEnvironmentVariable("FULLNET__MODULES__ENABLED__1", module);
        await AssertDiagnosis(fixture, profile, expected);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    public async Task Empty_parent_markers_do_not_remove_lower_unavailable_modules(string marker)
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Payments\"]}");
        var overlay = "{\"FullNet:Modules:Enabled\":" + marker + "}";
        fixture.WriteProfile("production", overlay);
        Assert.AreEqual(false, ExpectedAvailability(fixture.Configuration, overlay));
        await AssertDiagnosis(fixture, "production", false);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Production_does_not_read_development_secrets_for_installation_scope(bool valid)
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled\":[\"Identity\",\"" + (valid ? "Tenancy" : "Payments") + "\"]}");
        fixture.WriteSecrets("{\"FullNet:Modules:Enabled:1\":\"" + (valid ? "Payments" : "Tenancy") + "\"}");
        await AssertDiagnosis(fixture, "production", valid);
    }

    [TestMethod]
    public async Task Extra_module_project_and_reference_do_not_expand_the_frozen_installation_scope()
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Payments\"]}");
        fixture.Write("framework/fullnet/src/Modules/Full.NET.Modules.Payments/Full.NET.Modules.Payments.csproj", "<Project />");
        fixture.Write(Workspace.CompositionProject, fixture.ProjectXml("Payments"));
        await AssertDiagnosis(fixture, "production", false);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("42")]
    public async Task Unreadable_runtime_preset_members_cannot_be_certified_as_available(string members)
    {
        using var fixture = new Workspace();
        var manifest = JsonNode.Parse(File.ReadAllText(fixture.PathFor("framework-manifest.json")))!;
        manifest["presetModules"]!["platform"] = JsonNode.Parse(members);
        fixture.Write("framework-manifest.json", manifest.ToJsonString());
        fixture.WriteProfile("production", "{\"FullNet:Modules:Preset\":\"Platform\"}");
        await AssertDiagnosis(fixture, "production", false, "DIAG_RUNTIME_MODULES_METADATA_INVALID error");
    }

    [TestMethod]
    [DataRow("{\"FullNet:Modules:Enabled\":[]}", "DIAG_MODULE_ENABLED_INVALID error")]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"identity\"]}", "DIAG_MODULE_ENABLED_INVALID error")]
    [DataRow("{\"FullNet:Modules:Preset\":\"credential-probe\"}", "DIAG_MODULE_PRESET_INVALID error")]
    [DataRow("{\"FullNet:Modules:Preset\":\" \"}", "DIAG_MODULE_PRESET_INVALID error")]
    public async Task Invalid_selection_does_not_emit_an_installation_scope_success(string overlay, string finding)
    {
        using var fixture = new Workspace();
        fixture.WriteProfile("production", overlay);
        await AssertDiagnosis(fixture, "production", false, finding, applicable: false);
    }

    private static bool ExpectedAvailability(string configuration, string overlay)
    {
        using var runtime = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)))
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay))).Build();
        var options = runtime.GetSection("FullNet:Modules").Get<FullNetModuleSelectionOptions>() ?? new();
        // 投影器将 Full 的官方全集替换成冻结安装集；其他预设仍沿用运行时成员列表。
        var names = options.Enabled is null && string.Equals(options.Preset, "Full", StringComparison.OrdinalIgnoreCase)
            ? Workspace.Modules.ToHashSet(StringComparer.Ordinal)
            : FullNetModuleSelection.ResolveEnabledNames(runtime);
        return names.All(name => Workspace.Modules.Contains(name, StringComparer.Ordinal));
    }

    private static async Task AssertDiagnosis(Workspace fixture, string profile, bool available, string? expected = null, bool applicable = true)
    {
        var before = fixture.ReadFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        Assert.AreEqual(available ? 0 : 1, result, text);
        StringAssert.Contains(text, expected ?? (available ? "DIAG_RUNTIME_MODULES_AVAILABLE ok" : "DIAG_RUNTIME_MODULES_UNAVAILABLE error"));
        Assert.AreEqual(available, text.Contains("DIAG_RUNTIME_MODULES_AVAILABLE ok", StringComparison.Ordinal), text);
        if (!applicable) Assert.IsFalse(text.Contains("DIAG_RUNTIME_MODULES_", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "DIAG_MODULE_CLOSURE_OK ok");
        Assert.IsFalse(text.Contains("credential-probe", StringComparison.Ordinal));
        var after = fixture.ReadFiles();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }

    private sealed class Workspace : IDisposable
    {
        public static readonly string[] Modules = ["Identity", "Tenancy", "Organization", "Settings"];
        public const string CompositionProject = "framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-module-scope-" + Guid.NewGuid().ToString("N"));
        public string Configuration { get; }
        private string? secretsDirectory;

        public Workspace(string additional = "{}")
        {
            Configuration = "{\"Database\":{\"Provider\":\"MySql\",\"MySqlGuidStorageMode\":\"Binary16\",\"ConnectionString\":\"Server=example.invalid;Password=credential-probe\"},\"Identity\":{\"EnableTokenEndpoints\":false},\"FullNet:Modules:Preset\":\"Minimal\"," + (additional == "{}" ? "\"Probe\":1}" : additional[1..]);
            Write("fullnet-app.json", "{\"preset\":\"minimal\",\"databaseProvider\":\"mysql\"}");
            Write("framework-manifest.json", JsonSerializer.Serialize(new { presetModules = new Dictionary<string, IReadOnlyList<string>>
            {
                ["minimal"] = FullNetModuleSelection.MinimalPresetModuleNames,
                ["platform"] = FullNetModuleSelection.PlatformPresetModuleNames,
                ["saas"] = FullNetModuleSelection.SaasPresetModuleNames,
                ["enterprise"] = FullNetModuleSelection.EnterprisePresetModuleNames,
            }}));
            Write("global.json", "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"latestFeature\",\"allowPrerelease\":true}}");
            Write("appsettings.json", Configuration);
            Write("src/Demo.Host.Api/appsettings.json", Configuration);
            Write("src/Demo.Host.Api/Demo.Host.Api.csproj", "<Project />");
            Write(CompositionProject, ProjectXml());
            foreach (var module in Modules) Write("framework/fullnet/src/Modules/Full.NET.Modules." + module + "/Full.NET.Modules." + module + ".csproj", "<Project />");
        }

        public string ProjectXml(string? additional = null) => "<Project><ItemGroup>" + string.Join(string.Empty,
            Modules.Concat(additional is null ? [] : new[] { additional }).Select(module => "<ProjectReference Include=\"../../Modules/Full.NET.Modules." + module + "/Full.NET.Modules." + module + ".csproj\" />")) + "</ItemGroup></Project>";
        public string PathFor(string path) => Path.Combine(Root, path);
        public void Write(string path, string content)
        {
            var target = PathFor(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }
        public void WriteProfile(string profile, string content) => Write("src/Demo.Host.Api/appsettings." + (profile == "development" ? "Development" : "Production") + ".json", content);
        public void WriteSecrets(string content)
        {
            var id = "fullnet-module-scope-" + Guid.NewGuid().ToString("N");
            Write("src/Demo.Host.Api/Demo.Host.Api.csproj", "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory);
            File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }
        public Dictionary<string, byte[]> ReadFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Root, path), File.ReadAllBytes);
        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            if (secretsDirectory is not null) Directory.Delete(secretsDirectory, recursive: true);
        }
    }
}
