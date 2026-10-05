using System.Reflection;
using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class StandaloneModuleDependencyTests
{
    // 测试用真实组合根实例作判定器，生产 CLI 不加载或执行模块实现。
    private static readonly IReadOnlyDictionary<string, IFullNetModule> Official =
        ((IReadOnlyList<IFullNetModule>)typeof(FullNetModuleCatalog)
            .GetMethod("CreateAllModules", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!)
        .ToDictionary(module => module.Name, StringComparer.Ordinal);
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
    [DataRow("Identity")]
    [DataRow("Auditing")]
    [DataRow("Files")]
    [DataRow("Document")]
    [DataRow("Notifications")]
    [DataRow("Calendar")]
    [DataRow("Platform")]
    [DataRow("Regions")]
    [DataRow("Jobs")]
    [DataRow("Messaging")]
    [DataRow("Tenancy")]
    [DataRow("Organization")]
    [DataRow("ImportExport")]
    [DataRow("Reporting")]
    [DataRow("Printing")]
    [DataRow("Ai")]
    [DataRow("Settings")]
    [DataRow("CodeGeneration")]
    [DataRow("SerialNumbers")]
    [DataRow("DataApproval")]
    [DataRow("ObservabilityAdmin")]
    [DataRow("Workflow")]
    [DataRow("Mqtt")]
    [DataRow("Webhooks")]
    [DataRow("Cryptography")]
    [DataRow("Payments")]
    [DataRow("GoView")]
    [DataRow("K3Cloud")]
    [DataRow("Ocr")]
    [DataRow("EnterpriseRequest")]
    public async Task Official_literal_sources_match_the_actual_runtime_dependency_closure(string name)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        void Add(string module) { if (selected.Add(module)) foreach (var dependency in Official[module].Dependencies) Add(dependency); }
        Add(name);
        AssertRuntimeClosed(selected);
        using var fixture = new Workspace();
        fixture.Select(selected);
        await Diagnose(fixture, 0, "DIAG_RUNTIME_MODULE_DEPENDENCIES_CLOSED ok");
    }

    [TestMethod]
    [DataRow("Organization")]
    [DataRow("Ai")]
    [DataRow("Document")]
    [DataRow("Jobs")]
    [DataRow("SerialNumbers")]
    public async Task An_installed_module_cannot_omit_its_required_enabled_dependencies(string name)
    {
        using var fixture = new Workspace();
        fixture.Select(["Identity", name]);
        using var config = Config(["Identity", name]);
        Assert.ThrowsExactly<InvalidOperationException>(() => FullNetModuleSelection.ResolveEnabledModules(config, Official.Values.ToArray()));
        await Diagnose(fixture, 1, "DIAG_RUNTIME_MODULE_DEPENDENCIES_INVALID error");
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("blank")]
    [DataRow("unknown")]
    [DataRow("case")]
    [DataRow("self")]
    [DataRow("cycle")]
    [DataRow("optional-unknown")]
    [DataRow("optional-overlap")]
    [DataRow("name-mismatch")]
    public async Task Invalid_literal_declarations_and_cycles_are_reported_without_values(string kind)
    {
        using var fixture = new Workspace();
        fixture.Select(["Identity", "Tenancy", "Organization"]);
        var required = kind switch {
            "missing" => "[\"Files\"]", "duplicate" => "[\"Identity\",\"Identity\",\"Tenancy\"]",
            "blank" => "[\" \", \"Tenancy\"]", "unknown" => "[\"credential-probe\"]",
            "case" => "[\"identity\"]", "self" => "[\"Organization\"]",
            _ => "[\"Identity\",\"Tenancy\"]",
        };
        var optional = kind == "optional-unknown" ? "[\"credential-probe\"]" : kind == "optional-overlap" ? "[\"Identity\"]" : "[]";
        fixture.Source("Organization", Declaration("Organization", required, optional, kind == "name-mismatch" ? "organization" : null));
        if (kind == "cycle") fixture.Source("Tenancy", Declaration("Tenancy", "[\"Organization\"]"));
        await Diagnose(fixture, 1, "DIAG_RUNTIME_MODULE_DEPENDENCIES_INVALID error");
    }

    [TestMethod]
    [DataRow("method")]
    [DataRow("spread")]
    [DataRow("getter")]
    [DataRow("absent-property")]
    [DataRow("syntax")]
    [DataRow("conditional")]
    [DataRow("missing-source")]
    [DataRow("partial")]
    public async Task Unreadable_or_dynamic_declarations_remain_explicitly_unverified(string kind)
    {
        using var fixture = new Workspace();
        fixture.Select(["Identity", "Tenancy", "Organization"]);
        var source = Declaration("Organization", kind == "spread" ? "[..GetDependencies()]" : "GetDependencies()");
        if (kind == "getter") source = source.Replace("Dependencies => GetDependencies();", "Dependencies { get { return [\"Tenancy\"]; } }");
        if (kind == "absent-property") source = source.Replace("public IReadOnlyCollection<string> Dependencies => GetDependencies();", "");
        if (kind == "syntax") source = "public class { credential-probe";
        if (kind == "conditional") source = "#if SYMBOL\n" + Declaration("Organization", "[\"Identity\",\"Tenancy\"]") + "\n#endif";
        if (kind == "partial") source = Declaration("Organization", "[\"Identity\",\"Tenancy\"]").Replace("sealed class", "sealed partial class");
        if (kind == "missing-source") File.Delete(fixture.SourcePath("Organization")); else fixture.Source("Organization", source);
        await Diagnose(fixture, 0, "DIAG_RUNTIME_MODULE_DEPENDENCIES_UNVERIFIED warn");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unselected_sources_do_not_block_the_selected_static_graph(bool invalidLiteral)
    {
        using var fixture = new Workspace();
        fixture.Select(["Identity"]);
        fixture.Source("Organization", Declaration("Organization", invalidLiteral ? "[\"credential-probe\"]" : "GetDependencies()"));
        await Diagnose(fixture, 0, "DIAG_RUNTIME_MODULE_DEPENDENCIES_CLOSED ok");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Invalid_names_or_uninstalled_modules_do_not_emit_a_dependency_certificate(bool unavailable)
    {
        using var fixture = new Workspace(minimal: true);
        fixture.Select(["Identity", unavailable ? "Payments" : "credential-probe"]);
        await Diagnose(fixture, 1, unavailable ? "DIAG_RUNTIME_MODULES_UNAVAILABLE error" : "DIAG_MODULE_ENABLED_INVALID error", dependencyApplicable: false);
    }

    private static string Declaration(string module, string dependencies, string optional = "[]", string? name = null) =>
        "public sealed class " + module + "Module : IFullNetModule { public string Name => \"" + (name ?? module)
        + "\"; public IReadOnlyCollection<string> Dependencies => " + dependencies
        + "; public IReadOnlyCollection<string> OptionalContractDependencies => " + optional + "; }";

    private static ConfigurationRoot Config(IEnumerable<string> selected) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddInMemoryCollection(selected.Select((name, index) => new KeyValuePair<string, string?>("FullNet:Modules:Enabled:" + index, name))).Build();

    private static void AssertRuntimeClosed(IEnumerable<string> selected)
    {
        using var config = Config(selected);
        var modules = FullNetModuleSelection.ResolveEnabledModules(config, Official.Values.ToArray());
        var registry = new FullNetModuleRegistry();
        foreach (var module in modules) registry.Add(module);
        Assert.AreEqual(modules.Count, registry.GetOrderedModules().Count);
    }

    private static async Task Diagnose(Workspace fixture, int expectedExit, string finding, bool dependencyApplicable = true)
    {
        var before = fixture.ReadFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_APP_PROFILE_OK ok");
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        Assert.AreEqual(expectedExit, result, text);
        StringAssert.Contains(text, finding);
        if (!dependencyApplicable) Assert.IsFalse(text.Contains("DIAG_RUNTIME_MODULE_DEPENDENCIES_", StringComparison.Ordinal), text);
        else Assert.AreEqual(finding.EndsWith("CLOSED ok", StringComparison.Ordinal), text.Contains("DIAG_RUNTIME_MODULE_DEPENDENCIES_CLOSED ok", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("credential-probe", StringComparison.Ordinal));
        var after = fixture.ReadFiles();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var file in before) CollectionAssert.AreEqual(file.Value, after[file.Key], file.Key);
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-dependency-" + Guid.NewGuid().ToString("N"));

        public Workspace(bool minimal = false)
        {
            var modules = minimal ? new[] { "Identity", "Tenancy", "Organization", "Settings" } : Official.Keys.ToArray();
            Write("fullnet-app.json", "{\"preset\":\"minimal\",\"databaseProvider\":\"mysql\"}");
            Write("framework-manifest.json", JsonSerializer.Serialize(new { presetModules = new Dictionary<string, string[]> { ["minimal"] = modules } }));
            Write("global.json", "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"latestFeature\",\"allowPrerelease\":true}}");
            Write("src/Demo.Host.Api/Demo.Host.Api.csproj", "<Project />");
            Write("framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj",
                "<Project><ItemGroup>" + string.Join("", modules.Select(module => "<ProjectReference Include=\"../../Modules/Full.NET.Modules."
                    + module + "/Full.NET.Modules." + module + ".csproj\" />")) + "</ItemGroup></Project>");
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Directory.Packages.props"))) repo = repo.Parent;
            Assert.IsNotNull(repo);
            foreach (var module in modules)
            {
                Write("framework/fullnet/src/Modules/Full.NET.Modules." + module + "/Full.NET.Modules." + module + ".csproj", "<Project />");
                var relative = module == "EnterpriseRequest"
                    ? "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/EnterpriseRequestModule.cs"
                    : "src/Modules/Full.NET.Modules." + module + "/" + module + "Module.cs";
                Source(module, File.ReadAllText(Path.Combine(repo.FullName, relative)));
            }
            Select(["Identity"]);
        }

        public string SourcePath(string module) => Path.Combine(Root, "framework/fullnet", module == "EnterpriseRequest"
            ? "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/EnterpriseRequestModule.cs"
            : "src/Modules/Full.NET.Modules." + module + "/" + module + "Module.cs");
        public void Source(string module, string source) => Write(Path.GetRelativePath(Root, SourcePath(module)), source);
        public void Select(IEnumerable<string> selected)
        {
            var config = JsonSerializer.Serialize(new Dictionary<string, object> {
                ["Database"] = new { Provider = "MySql", MySqlGuidStorageMode = "Binary16", ConnectionString = "Server=example.invalid;Password=credential-probe" },
                ["Identity"] = new { EnableTokenEndpoints = false }, ["FullNet:Modules:Preset"] = "Minimal", ["FullNet:Modules:Enabled"] = selected.ToArray(),
            });
            Write("appsettings.json", config);
            Write("src/Demo.Host.Api/appsettings.json", config);
        }
        public void Write(string path, string content)
        {
            var target = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }
        public Dictionary<string, byte[]> ReadFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Root, path), File.ReadAllBytes);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

