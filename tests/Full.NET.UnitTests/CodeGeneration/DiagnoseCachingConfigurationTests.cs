using System.Text;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseCachingConfigurationTests
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
    [DataRow("development", "zero-duration")]
    [DataRow("development", "negative-duration")]
    [DataRow("development", "invalid-duration")]
    [DataRow("development", "negative-jitter")]
    [DataRow("development", "invalid-jitter")]
    [DataRow("development", "zero-jitter")]
    [DataRow("development", "invalid-redis")]
    [DataRow("development", "valid-redis")]
    [DataRow("development", "shared-redis")]
    [DataRow("development", "shared-allowed")]
    [DataRow("development", "invalid-entry")]
    [DataRow("production", "missing")]
    [DataRow("production", "valid")]
    [DataRow("production", "zero-duration")]
    [DataRow("production", "negative-duration")]
    [DataRow("production", "invalid-duration")]
    [DataRow("production", "negative-jitter")]
    [DataRow("production", "invalid-jitter")]
    [DataRow("production", "zero-jitter")]
    [DataRow("production", "invalid-redis")]
    [DataRow("production", "valid-redis")]
    [DataRow("production", "shared-redis")]
    [DataRow("production", "shared-allowed")]
    [DataRow("production", "invalid-entry")]
    public async Task Cache_diagnosis_matches_real_registration_without_resolving_connections(string profile, string shape)
    {
        var settings = Settings();
        var cache = settings["Cache"]!.AsObject();
        switch (shape)
        {
            case "missing": settings.Remove("Cache"); break;
            case "zero-duration": cache["DefaultDuration"] = "00:00:00"; break;
            case "negative-duration": cache["DefaultDuration"] = "-00:00:01"; break;
            case "invalid-duration": cache["DefaultDuration"] = "cache-probe"; break;
            case "negative-jitter": cache["Jitter"] = "-00:00:01"; break;
            case "invalid-jitter": cache["Jitter"] = "cache-probe"; break;
            case "zero-jitter": cache["Jitter"] = "00:00:00"; break;
            case "invalid-redis": cache["RedisConnectionString"] = "cache.invalid,password=cache-probe,connectTimeout=cache-probe"; break;
            case "valid-redis": cache["RedisConnectionString"] = "cache.invalid,password=cache-probe,connectTimeout=10"; break;
            case "shared-redis":
            case "shared-allowed":
                cache["RedisConnectionString"] = "cache.invalid,password=cache-probe";
                settings["Realtime"] = new JsonObject { ["RedisBackplaneConnectionString"] = cache["RedisConnectionString"]!.DeepClone(), ["AllowSharedRedisInDevelopment"] = shape == "shared-allowed" };
                break;
            case "invalid-entry":
                cache["Entries"] = new JsonObject { ["sample"] = new JsonObject { ["ConsistencyClass"] = "cache-probe" } };
                break;
        }
        using var fixture = new Workspace(settings.ToJsonString());
        var valid = shape is "missing" or "valid" or "zero-jitter" or "valid-redis" || shape == "shared-allowed" && profile == "development";
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, null, profile), "先核对真实缓存注册，不解析缓存或连接服务。");
        await AssertDiagnostic(fixture, profile, valid);
    }

    [TestMethod]
    [DataRow("profile", false)]
    [DataRow("profile", true)]
    [DataRow("secrets", false)]
    [DataRow("secrets", true)]
    [DataRow("environment", false)]
    [DataRow("environment", true)]
    public async Task Cache_diagnosis_uses_effective_profile_secrets_and_environment(string layer, bool valid)
    {
        var settings = Settings();
        settings["Cache"]!["DefaultDuration"] = valid ? "00:00:00" : "00:01:00";
        using var fixture = new Workspace(settings.ToJsonString());
        var value = valid ? "00:01:00" : "00:00:00";
        var overlay = new JsonObject { ["cAcHe:DefaultDuration"] = value }.ToJsonString();
        if (layer == "profile") fixture.WriteProfile("development", overlay);
        if (layer == "secrets") fixture.WriteSecrets(overlay);
        if (layer == "environment") Environment.SetEnvironmentVariable("Cache__DefaultDuration", value);
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, layer == "environment" ? null : overlay, "development"));
        await AssertDiagnostic(fixture, "development", valid);
    }

    [TestMethod]
    public async Task Production_cache_diagnosis_ignores_development_secret_repair()
    {
        var settings = Settings(); settings["Cache"]!["DefaultDuration"] = "00:00:00";
        using var fixture = new Workspace(settings.ToJsonString());
        fixture.WriteSecrets("""{"Cache:DefaultDuration":"00:01:00"}""");
        Assert.IsFalse(RuntimeValid(fixture.Configuration, null, "production"));
        await AssertDiagnostic(fixture, "production", false);
    }

    [TestMethod]
    public async Task Invalid_secret_type_keeps_existing_configuration_error()
    {
        var settings = Settings(); settings["Cache"]!["RedisConnectionString"] = true;
        using var fixture = new Workspace(settings.ToJsonString());
        var result = await Diagnose(fixture, "production");
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APPSETTINGS_INVALID error");
        Assert.IsFalse(result.Output.Contains("code_generation.cache.configuration.", StringComparison.Ordinal));
    }

    private static JsonObject Settings() => new()
    {
        ["Database"] = new JsonObject { ["Provider"] = "SqlServer", ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionString"] = "Server=example.invalid;Password=cache-probe" },
        ["Cache"] = new JsonObject { ["DefaultDuration"] = "00:05:00", ["Jitter"] = "00:00:30" },
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
            _ = new ServiceCollection().AddFullNetCaching(root, profile == "production" ? "Production" : "Development");
            return true;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return false; }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, bool valid)
    {
        var result = await Diagnose(fixture, profile);
        Assert.AreEqual(valid ? 0 : 1, result.ExitCode, result.Output);
        if (valid) Assert.IsFalse(result.Output.Contains("code_generation.cache.configuration.invalid", StringComparison.Ordinal));
        else StringAssert.Contains(result.Output, "code_generation.cache.configuration.invalid error");
    }

    private static async Task<(int ExitCode, string Output)> Diagnose(Workspace fixture, string profile)
    {
        var before = fixture.Files.ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        Assert.IsFalse(text.Contains("cache-probe", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), fixture.Files);
        foreach (var pair in before) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key));
        return (exit, text);
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-cache-diagnose-" + Guid.NewGuid().ToString("N"));
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
            var id = "fullnet-cache-diagnose-" + Guid.NewGuid().ToString("N");
            var project = Path.Combine(Root, "src", "Demo.Host.Api");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Demo.Host.Api.csproj"), "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory); File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }
        public void Dispose() { Directory.Delete(Root, true); if (secretsDirectory is not null) Directory.Delete(secretsDirectory, true); }
    }
}
