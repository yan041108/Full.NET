using System.Text;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Realtime.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseRealtimeTransportTests
{
    private readonly Dictionary<string, string?> originalEnvironment = new(StringComparer.Ordinal);

    [TestInitialize]
    public void Isolate_configuration()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key?.ToString() is { } key && Relevant(key))
            {
                originalEnvironment[key] = entry.Value?.ToString();
                Environment.SetEnvironmentVariable(key, null);
            }
    }

    [TestCleanup]
    public void Restore_configuration()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key?.ToString() is { } key && Relevant(key)) Environment.SetEnvironmentVariable(key, null);
        foreach (var pair in originalEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private static bool Relevant(string key)
    {
        var path = key.Replace("__", ":", StringComparison.Ordinal);
        return new[] { "Cache", "Realtime", "Database", "DatabaseCapacity", "ConnectionStrings", "Identity", "FullNet:Modules" }
            .Any(section => path.Equals(section, StringComparison.OrdinalIgnoreCase) || path.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase))
            || new[] { "MYSQLCONNSTR_", "SQLCONNSTR_", "SQLAZURECONNSTR_", "CUSTOMCONNSTR_" }
                .Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

[TestMethod]
    [DataRow("development", "missing")]
    [DataRow("development", "valid")]
    [DataRow("development", "hub-empty")]
    [DataRow("development", "hub-relative")]
    [DataRow("development", "hub-root")]
    [DataRow("development", "hub-trailing")]
    [DataRow("development", "hub-double-slash")]
    [DataRow("development", "hub-query")]
    [DataRow("development", "hub-fragment")]
    [DataRow("development", "hub-space")]
    [DataRow("development", "mode-invalid")]
    [DataRow("development", "mode-empty")]
    [DataRow("development", "mode-undefined")]
    [DataRow("development", "mode-combination")]
    [DataRow("development", "default-skip")]
    [DataRow("development", "default-no-affinity")]
    [DataRow("development", "ws-negotiate-affinity")]
    [DataRow("development", "ws-skip-affinity")]
    [DataRow("development", "ws-skip-no-affinity")]
    [DataRow("development", "ws-negotiate-no-affinity")]
    [DataRow("development", "enabled-invalid")]
    [DataRow("development", "skip-invalid")]
    [DataRow("development", "affinity-invalid")]
    [DataRow("development", "shared-invalid")]
    [DataRow("development", "disabled")]
    [DataRow("development", "disabled-invalid-hub")]
    [DataRow("development", "disabled-invalid-transport")]
    [DataRow("development", "disabled-invalid-bool")]
    [DataRow("production", "missing")]
    [DataRow("production", "valid")]
    [DataRow("production", "hub-empty")]
    [DataRow("production", "hub-relative")]
    [DataRow("production", "hub-root")]
    [DataRow("production", "hub-trailing")]
    [DataRow("production", "hub-double-slash")]
    [DataRow("production", "hub-query")]
    [DataRow("production", "hub-fragment")]
    [DataRow("production", "hub-space")]
    [DataRow("production", "mode-invalid")]
    [DataRow("production", "mode-empty")]
    [DataRow("production", "mode-undefined")]
    [DataRow("production", "mode-combination")]
    [DataRow("production", "default-skip")]
    [DataRow("production", "default-no-affinity")]
    [DataRow("production", "ws-negotiate-affinity")]
    [DataRow("production", "ws-skip-affinity")]
    [DataRow("production", "ws-skip-no-affinity")]
    [DataRow("production", "ws-negotiate-no-affinity")]
    [DataRow("production", "enabled-invalid")]
    [DataRow("production", "skip-invalid")]
    [DataRow("production", "affinity-invalid")]
    [DataRow("production", "shared-invalid")]
    [DataRow("production", "disabled")]
    [DataRow("production", "disabled-invalid-hub")]
    [DataRow("production", "disabled-invalid-transport")]
    [DataRow("production", "disabled-invalid-bool")]
    public async Task Realtime_transport_diagnosis_matches_real_registration(string profile, string shape)
    {
        var settings = Settings();
        var realtime = settings["Realtime"]!.AsObject();
        switch (shape)
        {
            case "missing": settings.Remove("Realtime"); break;
            case "hub-empty": realtime["HubPath"] = ""; break;
            case "hub-relative": realtime["HubPath"] = "realtime-probe"; break;
            case "hub-root": realtime["HubPath"] = "/"; break;
            case "hub-trailing": realtime["HubPath"] = "/realtime-probe/"; break;
            case "hub-double-slash": realtime["HubPath"] = "/realtime-probe//hub"; break;
            case "hub-query": realtime["HubPath"] = "/hub?realtime-probe"; break;
            case "hub-fragment": realtime["HubPath"] = "/hub#realtime-probe"; break;
            case "hub-space": realtime["HubPath"] = "/hub realtime-probe"; break;
            case "mode-invalid": realtime["TransportMode"] = "realtime-probe"; break;
            case "mode-empty": realtime["TransportMode"] = ""; break;
            case "mode-undefined": realtime["TransportMode"] = 99; break;
            case "mode-combination": realtime["TransportMode"] = "Default, WebSocketsOnly"; break;
            case "default-skip": realtime["SkipNegotiation"] = true; break;
            case "default-no-affinity": realtime["RequireSessionAffinity"] = false; break;
            case "ws-negotiate-affinity": realtime["TransportMode"] = "WebSocketsOnly"; break;
            case "ws-skip-affinity":
            case "ws-skip-no-affinity":
            case "ws-negotiate-no-affinity":
                realtime["TransportMode"] = "WebSocketsOnly";
                realtime["SkipNegotiation"] = shape != "ws-negotiate-no-affinity";
                realtime["RequireSessionAffinity"] = shape == "ws-skip-affinity";
                break;
            case "enabled-invalid": realtime["Enabled"] = "realtime-probe"; break;
            case "skip-invalid": realtime["SkipNegotiation"] = "realtime-probe"; break;
            case "affinity-invalid": realtime["RequireSessionAffinity"] = "realtime-probe"; break;
            case "shared-invalid": realtime["AllowSharedRedisInDevelopment"] = "realtime-probe"; break;
            case "disabled": realtime["Enabled"] = false; break;
            case "disabled-invalid-hub": realtime["Enabled"] = false; realtime["HubPath"] = "realtime-probe"; break;
            case "disabled-invalid-transport": realtime["Enabled"] = false; realtime["SkipNegotiation"] = true; realtime["RequireSessionAffinity"] = false; break;
            case "disabled-invalid-bool": realtime["Enabled"] = false; realtime["SkipNegotiation"] = "realtime-probe"; break;
        }
        using var fixture = new Workspace(settings.ToJsonString());
        var valid = shape is "missing" or "valid" or "mode-undefined" or "mode-combination"
            or "ws-negotiate-affinity" or "ws-skip-affinity" or "ws-skip-no-affinity"
            or "disabled" or "disabled-invalid-hub" or "disabled-invalid-transport";
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, null, profile), "先核对真实 API 注册，不解析 Hub 或连接。");
        await AssertDiagnostic(fixture, profile, valid);
    }

    [TestMethod]
    [DataRow("profile", false)]
    [DataRow("profile", true)]
    [DataRow("secrets", false)]
    [DataRow("secrets", true)]
    [DataRow("environment", false)]
    [DataRow("environment", true)]
    public async Task Realtime_transport_uses_effective_configuration(string layer, bool valid)
    {
        var settings = Settings();
        settings["Realtime"]!["HubPath"] = valid ? "realtime-probe" : "/hubs/notifications";
        using var fixture = new Workspace(settings.ToJsonString());
        var value = valid ? "/hubs/notifications" : "realtime-probe";
        var overlay = new JsonObject { ["rEaLtImE:HubPath"] = value }.ToJsonString();
        if (layer == "profile") fixture.WriteProfile("development", overlay);
        if (layer == "secrets") fixture.WriteSecrets(overlay);
        if (layer == "environment") Environment.SetEnvironmentVariable("Realtime__HubPath", value);
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, layer == "environment" ? null : overlay, "development"));
        await AssertDiagnostic(fixture, "development", valid);
    }

    [TestMethod]
    public async Task Production_realtime_transport_ignores_development_secret_repair()
    {
        var settings = Settings(); settings["Realtime"]!["HubPath"] = "realtime-probe";
        using var fixture = new Workspace(settings.ToJsonString());
        fixture.WriteSecrets("""{"Realtime:HubPath":"/hubs/notifications"}""");
        Assert.IsFalse(RuntimeValid(fixture.Configuration, null, "production"));
        await AssertDiagnostic(fixture, "production", false);
    }

    [TestMethod]
    public async Task Invalid_secret_type_keeps_existing_configuration_error()
    {
        var settings = Settings(); settings["Realtime"]!["RedisBackplaneConnectionString"] = true;
        using var fixture = new Workspace(settings.ToJsonString());
        var result = await Diagnose(fixture, "production");
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APPSETTINGS_INVALID error");
        Assert.IsFalse(result.Output.Contains("code_generation.realtime.transport.", StringComparison.Ordinal));
    }

    private static JsonObject Settings() => new()
    {
        ["Database"] = new JsonObject { ["Provider"] = "SqlServer", ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionString"] = "Server=example.invalid;Password=realtime-probe" },
        ["Realtime"] = new JsonObject { ["Enabled"] = true, ["HubPath"] = "/hubs/notifications", ["TransportMode"] = "Default", ["SkipNegotiation"] = false, ["RequireSessionAffinity"] = true, ["AllowSharedRedisInDevelopment"] = false },
        ["FullNet"] = new JsonObject { ["Modules"] = new JsonObject { ["Preset"] = "minimal" } },
        ["Identity"] = new JsonObject { ["EnableTokenEndpoints"] = false, ["Oidc"] = new JsonObject { ["Enable"] = false } },
    };

    private static bool RuntimeValid(string configuration, string? overlay, string profile)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        builder.AddEnvironmentVariables();
        var root = builder.Build();
        using var lifetime = root as IDisposable;
        try
        {
            _ = new ServiceCollection().AddFullNetRealtimeSignalR(root, profile == "production" ? "Production" : "Development");
            return true;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return false; }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, bool valid)
    {
        var result = await Diagnose(fixture, profile);
        Assert.AreEqual(valid ? 0 : 1, result.ExitCode, result.Output);
        if (valid) Assert.IsFalse(result.Output.Contains("code_generation.realtime.transport.invalid", StringComparison.Ordinal));
        else StringAssert.Contains(result.Output, "code_generation.realtime.transport.invalid error");
    }

    private static async Task<(int ExitCode, string Output)> Diagnose(Workspace fixture, string profile)
    {
        var before = fixture.Files.ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        Assert.IsFalse(text.Contains("realtime-probe", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), fixture.Files);
        foreach (var pair in before) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key));
        return (exit, text);
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-realtime-diagnose-" + Guid.NewGuid().ToString("N"));
        public string Configuration { get; }
        private string? secretsDirectory;
        public string[] Files => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Concat(secretsDirectory is null ? [] : Directory.EnumerateFiles(secretsDirectory)).ToArray();
        public Workspace(string configuration)
        {
            Directory.CreateDirectory(Root); Configuration = configuration;
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), configuration);
            File.WriteAllText(Path.Combine(Root, "global.json"), """{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":true}}""");
        }
        public void WriteProfile(string profile, string content) => File.WriteAllText(Path.Combine(Root, "appsettings." + (profile == "production" ? "Production" : "Development") + ".json"), content);
        public void WriteSecrets(string content)
        {
            var id = "fullnet-realtime-diagnose-" + Guid.NewGuid().ToString("N");
            var project = Path.Combine(Root, "src", "Demo.Host.Api");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Demo.Host.Api.csproj"), "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory); File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }
        public void Dispose() { Directory.Delete(Root, true); if (secretsDirectory is not null) Directory.Delete(secretsDirectory, true); }
    }
}
