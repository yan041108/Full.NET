using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Composition;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseModulePresetTests
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
    [DataRow("{\"FullNet:Modules:Preset\":null}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\" \"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"module-credential-probe\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":false}")]
    [DataRow("{\"FullNet:Modules:Preset\":42}")]
    [DataRow("{\"FullNet:Modules:Preset\":{}}")]
    [DataRow("{\"FullNet:Modules:Preset\":[]}")]
    [DataRow("{\"FullNet:Modules:Preset\":{\"Probe\":1}}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Full\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"minimal\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"PLATFORM\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Content\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Saas\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\"Enterprise\"}")]
    [DataRow("{\"FullNet:Modules:Preset\":\" minimal \"}")]
    [DataRow("{\"fullnet\":{\"modules\":{\"preset\":\"Minimal\"}}}")]
    [DataRow("{\"FULLNET:MODULES:PRESET\":\"Minimal\"}")]
    [DataRow("{\"FullNet:Modules\":null}")]
    public async Task Preset_diagnosis_matches_real_module_resolution(string configuration)
    {
        using var fixture = new Workspace(configuration);
        await AssertDiagnostic(fixture, "production", ExpectedFinding(fixture.Configuration));
    }

    [TestMethod]
    [DataRow("base", "development")]
    [DataRow("profile", "development")]
    [DataRow("secrets", "development")]
    [DataRow("environment", "development")]
    [DataRow("base", "production")]
    [DataRow("profile", "production")]
    [DataRow("environment", "production")]
    public async Task Preset_sources_override_invalid_lower_configuration(string source, string profile)
    {
        const string valid = """{"FULLNET:MODULES:PRESET":"Minimal"}""";
        using var fixture = new Workspace(source == "base" ? valid : """{"FullNet:Modules:Preset":"module-credential-probe"}""");
        Assert.AreEqual("DIAG_MODULE_PRESET_CONFIGURED ok", ExpectedFinding(fixture.Configuration, source == "base" ? null : valid));
        if (source == "profile") fixture.WriteProfile(profile, valid);
        if (source == "secrets")
        {
            fixture.WriteProfile(profile, """{"FullNet:Modules:Preset":"module-credential-probe"}""");
            fixture.WriteSecrets(valid);
        }
        if (source == "environment")
        {
            fixture.WriteProfile(profile, """{"FullNet:Modules:Preset":"module-credential-probe"}""");
            if (profile == "development") fixture.WriteSecrets("""{"FullNet:Modules:Preset":"module-credential-probe"}""");
            Environment.SetEnvironmentVariable("FULLNET__MODULES__PRESET", "Minimal");
        }
        await AssertDiagnostic(fixture, profile, "DIAG_MODULE_PRESET_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("secrets")]
    [DataRow("environment")]
    public async Task Empty_preset_override_does_not_restore_lower_valid_preset(string source)
    {
        using var fixture = new Workspace("""{"FullNet:Modules:Preset":"Minimal"}""");
        const string overlay = """{"FullNet:Modules:Preset":""}""";
        Assert.AreEqual("DIAG_MODULE_PRESET_INVALID error", ExpectedFinding(fixture.Configuration, overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteSecrets("""{"FullNet:Modules:Preset":"Platform"}""");
            Environment.SetEnvironmentVariable("FullNet__Modules__Preset", "");
        }
        await AssertDiagnostic(fixture, "development", "DIAG_MODULE_PRESET_INVALID error");
    }

    [TestMethod]
    [DataRow("Minimal", "module-credential-probe")]
    [DataRow("module-credential-probe", "Minimal")]
    public async Task Production_ignores_development_preset_secrets(string preset, string secret)
    {
        using var fixture = new Workspace(JsonSerializer.Serialize(new Dictionary<string, string> { ["FullNet:Modules:Preset"] = preset }));
        fixture.WriteSecrets(JsonSerializer.Serialize(new Dictionary<string, string> { ["FullNet:Modules:Preset"] = secret }));
        await AssertDiagnostic(fixture, "production", ExpectedFinding(fixture.Configuration));
    }

    [TestMethod]
    [DataRow("{\"FullNet:Modules:Enabled\":[\"Identity\",\"Tenancy\",\"Settings\",\"Organization\"]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":[]}")]
    [DataRow("{\"FullNet:Modules:Enabled\":null}")]
    [DataRow("{\"FullNet:Modules:Enabled\":{}}")]
    [DataRow("{\"FullNet:Modules:Enabled:first\":\"Identity\"}")]
    [DataRow("{\"FullNet:Modules:Enabled:0\":null}")]
    [DataRow("{\"FullNet:Modules:Enabled\":\"module-credential-probe\"}")]
    [DataRow("{\"FullNet:Modules:Enabled\":\" \"}")]
    [DataRow("{\"FullNet:Modules:Enabled\":false}")]
    [DataRow("{\"FullNet:Modules:Enabled\":42}")]
    [DataRow("{\"FullNet:Modules:Enabled\":\"\"}")]
    public async Task Explicit_enabled_selection_determines_whether_preset_applies(string enabled)
    {
        var config = "{\"FullNet:Modules:Preset\":\"module-credential-probe\"," + enabled[1..];
        using var fixture = new Workspace(config);
        await AssertDiagnostic(fixture, "production", ExpectedFinding(fixture.Configuration));
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("secrets")]
    [DataRow("environment")]
    public async Task Enabled_from_higher_source_overrides_invalid_preset(string source)
    {
        using var fixture = new Workspace("""{"FullNet:Modules:Preset":"module-credential-probe"}""");
        const string overlay = """{"FULLNET:MODULES:ENABLED:0":"Identity","FullNet:Modules:Enabled:1":"Tenancy","FullNet:Modules:Enabled:2":"Settings","FullNet:Modules:Enabled:3":"Organization"}""";
        Assert.AreEqual("DIAG_MODULE_ENABLED_CONFIGURED ok", ExpectedFinding(fixture.Configuration, overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, string>>(overlay)!)
                Environment.SetEnvironmentVariable(entry.Key.Replace(":", "__"), entry.Value);
        await AssertDiagnostic(fixture, "development", "DIAG_MODULE_ENABLED_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    public async Task Empty_enabled_parent_does_not_erase_lower_children(string marker)
    {
        using var fixture = new Workspace("""{"FullNet:Modules:Preset":"module-credential-probe","FullNet:Modules:Enabled":["Identity","Tenancy","Settings","Organization"]}""");
        var overlay = "{\"FullNet:Modules:Enabled\":" + marker + "}";
        Assert.AreEqual("DIAG_MODULE_ENABLED_CONFIGURED ok", ExpectedFinding(fixture.Configuration, overlay));
        fixture.WriteProfile("development", overlay);
        await AssertDiagnostic(fixture, "development", "DIAG_MODULE_ENABLED_CONFIGURED ok");
    }

    [TestMethod]
    public async Task Every_runtime_declared_preset_remains_supported()
    {
        var presets = typeof(FullNetModuleSelectionOptions.Presets)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();
        Assert.IsTrue(presets.Length > 0);
        foreach (var preset in presets)
        {
            using var fixture = new Workspace(JsonSerializer.Serialize(new Dictionary<string, string> { ["FullNet:Modules:Preset"] = preset }));
            Assert.AreEqual("DIAG_MODULE_PRESET_CONFIGURED ok", ExpectedFinding(fixture.Configuration));
            await AssertDiagnostic(fixture, "production", "DIAG_MODULE_PRESET_CONFIGURED ok");
        }
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("[null]")]
    [DataRow("[\"\"]")]
    [DataRow("[\" \" ,\"Identity\"]")]
    [DataRow("[\"Identity\",\"module-credential-probe\"]")]
    [DataRow("[\"Identity\",\"Identity\"]")]
    [DataRow("[\"Identity\",\"Tenancy\",\"Tenancy\"]")]
    [DataRow("[\"identity\"]")]
    [DataRow("[\"Identity\",\"TENANCY\"]")]
    [DataRow("[\" Identity\"]")]
    [DataRow("[\"Identity \"]")]
    [DataRow("[\"Tenancy\",\"Settings\"]")]
    [DataRow("[\"Identity\"]")]
    [DataRow("[\"Organization\",\"Identity\",\"Tenancy\",\"Settings\"]")]
    [DataRow("[\"Identity\",false]")]
    [DataRow("[\"Identity\",42]")]
    [DataRow("[\"Identity\",{}]")]
    [DataRow("[\"Identity\",[]]")]
    [DataRow("[\"Identity\",{\"Probe\":\"module-credential-probe\"}]")]
    [DataRow("[{\"Probe\":\"module-credential-probe\"}]")]
    [DataRow("{\"named\":\"Identity\",\"other\":\"Tenancy\"}")]
    [DataRow("{\"10\":\"Tenancy\",\"2\":\"Identity\"}")]
    [DataRow("{\"first\":\"Identity\",\"duplicate\":\"Identity\"}")]
    [DataRow("{\"0\":\"Identity\",\"1\":null}")]
    public async Task Enabled_name_diagnosis_matches_real_binding_and_resolution(string enabled)
    {
        // 使用路径键绕过工作区结构提示，直接比较宿主 Binder 的数组语义。
        using var fixture = new Workspace("{\"FullNet:Modules:Preset\":\"module-credential-probe\",\"FullNet:Modules:Enabled\":" + enabled + "}");
        await AssertDiagnostic(fixture, "production", ExpectedFinding(fixture.Configuration));
    }

    [TestMethod]
    public async Task Every_official_module_name_remains_accepted_without_certifying_dependencies()
    {
        foreach (var name in FullNetModuleSelection.OfficialModuleNames)
        {
            var names = name == "Identity" ? new[] { name } : new[] { "Identity", name };
            using var fixture = new Workspace(JsonSerializer.Serialize(new Dictionary<string, object> { ["FullNet:Modules:Enabled"] = names }));
            Assert.AreEqual("DIAG_MODULE_ENABLED_CONFIGURED ok", ExpectedFinding(fixture.Configuration));
            await AssertDiagnostic(fixture, "production", "DIAG_MODULE_ENABLED_CONFIGURED ok");
        }
    }

    [TestMethod]
    [DataRow("profile", true)]
    [DataRow("profile", false)]
    [DataRow("secrets", true)]
    [DataRow("secrets", false)]
    [DataRow("environment", true)]
    [DataRow("environment", false)]
    public async Task Enabled_children_merge_with_lower_sources_instead_of_replacing_the_array(string source, bool valid)
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled:0\":\"Identity\",\"FullNet:Modules:Enabled:1\":\"module-credential-probe\"}");
        var overlay = JsonSerializer.Serialize(new Dictionary<string, string> { ["FULLNET:MODULES:ENABLED:1"] = valid ? "Tenancy" : "Identity" });
        var expected = ExpectedFinding(fixture.Configuration, overlay);
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment") Environment.SetEnvironmentVariable("FULLNET__MODULES__ENABLED__1", valid ? "Tenancy" : "Identity");
        await AssertDiagnostic(fixture, "development", expected);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Production_ignores_development_enabled_secrets(bool valid)
    {
        using var fixture = new Workspace("{\"FullNet:Modules:Enabled\":[\"Identity\",\"" + (valid ? "Tenancy" : "module-credential-probe") + "\"]}");
        fixture.WriteSecrets("{\"FullNet:Modules:Enabled:1\":\"" + (valid ? "module-credential-probe" : "Tenancy") + "\"}");
        await AssertDiagnostic(fixture, "production", ExpectedFinding(fixture.Configuration));
    }

    private static string ExpectedFinding(string configuration, string? overlay = null)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        var runtime = builder.Build();
        var options = runtime.GetSection("FullNet:Modules").Get<FullNetModuleSelectionOptions>() ?? new();
        // 真实名称解析是独立判定器；列表合法不代表实现已安装或依赖闭包成立。
        var prefix = options.Enabled is null ? "DIAG_MODULE_PRESET_" : "DIAG_MODULE_ENABLED_";
        try
        {
            _ = FullNetModuleSelection.ResolveEnabledNames(runtime);
            return prefix + "CONFIGURED ok";
        }
        catch (InvalidOperationException) { return prefix + "INVALID error"; }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, string? finding)
    {
        var before = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        if (finding is not null) StringAssert.Contains(text, finding);
        else Assert.IsFalse(text.Contains("DIAG_MODULE_PRESET_", StringComparison.Ordinal), text);
        if (finding?.StartsWith("DIAG_MODULE_ENABLED_", StringComparison.Ordinal) == true)
            Assert.IsFalse(text.Contains("DIAG_MODULE_PRESET_", StringComparison.Ordinal), text);
        Assert.AreEqual(finding?.EndsWith(" error", StringComparison.Ordinal) == true ? 1 : 0, result, text);
        Assert.IsFalse(text.Contains("credential-probe", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, File.ReadAllBytes(entry.Key));
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-module-preset-" + Guid.NewGuid().ToString("N"));
        public string Configuration { get; }
        private string? secretsDirectory;

        public Workspace(string modules)
        {
            Directory.CreateDirectory(Root);
            Configuration = """{"Database":{"Provider":"SqlServer","MySqlGuidStorageMode":"Binary16","ConnectionString":"Server=example.invalid;Password=connection-credential-probe"},"Identity":{"EnableTokenEndpoints":false},""" + modules[1..];
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), Configuration);
            File.WriteAllText(Path.Combine(Root, "global.json"), """{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":true}}""");
        }

        public void WriteProfile(string profile, string content) => File.WriteAllText(
            Path.Combine(Root, "appsettings." + (profile == "development" ? "Development" : "Production") + ".json"), content);

        public void WriteSecrets(string content)
        {
            var id = "fullnet-module-preset-" + Guid.NewGuid().ToString("N");
            var project = Path.Combine(Root, "src", "App.Host.Api");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "App.Host.Api.csproj"), "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory);
            File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }

        public void Dispose()
        {
            Directory.Delete(Root, true);
            if (secretsDirectory is not null) Directory.Delete(secretsDirectory, true);
        }
    }
}
