using System.Text;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseDatabaseCapacityTests
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
        return new[] { "Database:", "DatabaseCapacity:", "ConnectionStrings:", "Identity:", "FullNet:Modules", "MYSQLCONNSTR_", "SQLCONNSTR_", "SQLAZURECONNSTR_", "CUSTOMCONNSTR_" }
            .Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("SqlServer", "valid")]
    [DataRow("SqlServer", "missing")]
    [DataRow("SqlServer", "disabled")]
    [DataRow("SqlServer", "invalid-enabled")]
    [DataRow("SqlServer", "invalid-role")]
    [DataRow("SqlServer", "invalid-permit")]
    [DataRow("SqlServer", "invalid-queue")]
    [DataRow("SqlServer", "invalid-timeout")]
    [DataRow("SqlServer", "pool-mismatch")]
    [DataRow("SqlServer", "role-pool-mismatch")]
    [DataRow("SqlServer", "reserve-overflow")]
    [DataRow("SqlServer", "cluster-budget")]
    [DataRow("SqlServer", "pooling-disabled")]
    [DataRow("SqlServer", "invalid-number")]
    [DataRow("SqlServer", "disabled-out-of-range")]
    [DataRow("SqlServer", "disabled-invalid-number")]
    [DataRow("MySql", "valid")]
    [DataRow("MySql", "missing")]
    [DataRow("MySql", "disabled")]
    [DataRow("MySql", "invalid-enabled")]
    [DataRow("MySql", "invalid-role")]
    [DataRow("MySql", "invalid-permit")]
    [DataRow("MySql", "invalid-queue")]
    [DataRow("MySql", "invalid-timeout")]
    [DataRow("MySql", "pool-mismatch")]
    [DataRow("MySql", "role-pool-mismatch")]
    [DataRow("MySql", "reserve-overflow")]
    [DataRow("MySql", "cluster-budget")]
    [DataRow("MySql", "pooling-disabled")]
    [DataRow("MySql", "invalid-number")]
    [DataRow("MySql", "disabled-out-of-range")]
    [DataRow("MySql", "disabled-invalid-number")]
    public async Task Budget_diagnosis_matches_real_options_without_opening_connections(string provider, string shape)
    {
        var settings = Settings(provider);
        var budget = settings["DatabaseCapacity"]!.AsObject();
        switch (shape)
        {
            case "missing": settings.Remove("DatabaseCapacity"); break;
            case "disabled": budget["Enabled"] = null; break;
            case "invalid-enabled": budget["Enabled"] = "budget-probe"; break;
            case "invalid-role": budget["HostRole"] = 999; break;
            case "invalid-permit": budget["PermitLimit"] = 0; break;
            case "invalid-queue": budget["QueueLimit"] = 1001; break;
            case "invalid-timeout": budget["AcquireTimeoutMilliseconds"] = 0; break;
            case "pool-mismatch": budget["ExpectedMaxPoolSize"] = 11; break;
            case "role-pool-mismatch": budget["ApiMaxPoolSize"] = 11; break;
            case "reserve-overflow": budget["HealthReserve"] = int.MaxValue; budget["CriticalWorkerReserve"] = int.MaxValue; break;
            case "cluster-budget": budget["TotalBudget"] = 31; break;
            case "pooling-disabled": settings["Database"]!["ConnectionString"] = settings["Database"]!["ConnectionString"]!.GetValue<string>() + ";Pooling=false"; break;
            case "invalid-number": budget["QueueLimit"] = "budget-probe"; break;
            case "disabled-out-of-range": budget["Enabled"] = false; budget["PermitLimit"] = -1; break;
            case "disabled-invalid-number": budget["Enabled"] = false; budget["QueueLimit"] = "budget-probe"; break;
        }
        using var fixture = new Workspace(settings.ToJsonString());
        var valid = shape is "valid" or "missing" or "disabled" or "disabled-out-of-range";
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, null, "production"), "先核对真实预算绑定与校验，不创建或打开数据库连接。");
        await AssertDiagnostic(fixture, "production", valid);
    }

    [TestMethod]
    [DataRow("SqlServer", "profile", false)]
    [DataRow("SqlServer", "profile", true)]
    [DataRow("SqlServer", "secrets", false)]
    [DataRow("SqlServer", "secrets", true)]
    [DataRow("SqlServer", "environment", false)]
    [DataRow("SqlServer", "environment", true)]
    [DataRow("MySql", "profile", false)]
    [DataRow("MySql", "profile", true)]
    [DataRow("MySql", "secrets", false)]
    [DataRow("MySql", "secrets", true)]
    [DataRow("MySql", "environment", false)]
    [DataRow("MySql", "environment", true)]
    public async Task Budget_diagnosis_uses_effective_profile_secrets_and_environment(string provider, string layer, bool valid)
    {
        var settings = Settings(provider);
        settings["DatabaseCapacity"]!["PermitLimit"] = valid ? 0 : 5;
        // 命名连接与直配都必须读取相同的实际池参数。
        if (valid)
        {
            settings["ConnectionStrings"] = new JsonObject { ["selected"] = settings["Database"]!["ConnectionString"]!.DeepClone() };
            settings["Database"]!["ConnectionName"] = "selected";
            settings["Database"]!["ConnectionString"] = "";
        }
        using var fixture = new Workspace(settings.ToJsonString());
        var value = valid ? 5 : 0;
        var overlay = new JsonObject { ["dAtAbAsEcApAcItY:PermitLimit"] = value }.ToJsonString();
        if (layer == "profile") fixture.WriteProfile("development", overlay);
        if (layer == "secrets") fixture.WriteSecrets(overlay);
        if (layer == "environment") Environment.SetEnvironmentVariable("DatabaseCapacity__PermitLimit", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, layer == "environment" ? null : overlay, "development"));
        await AssertDiagnostic(fixture, "development", valid);
    }

    [TestMethod]
    [DataRow("SqlServer")]
    [DataRow("MySql")]
    public async Task Production_budget_diagnosis_ignores_development_secret_repair(string provider)
    {
        var settings = Settings(provider); settings["DatabaseCapacity"]!["PermitLimit"] = 0;
        using var fixture = new Workspace(settings.ToJsonString());
        fixture.WriteSecrets("""{"DatabaseCapacity:PermitLimit":5}""");
        Assert.IsFalse(RuntimeValid(fixture.Configuration, null, "production"));
        await AssertDiagnostic(fixture, "production", false);
    }

    [TestMethod]
    [DataRow("SqlServer", "Provider")]
    [DataRow("SqlServer", "CommandTimeoutSeconds")]
    [DataRow("SqlServer", "MySqlGuidStorageMode")]
    [DataRow("MySql", "Provider")]
    [DataRow("MySql", "CommandTimeoutSeconds")]
    [DataRow("MySql", "MySqlGuidStorageMode")]
    public async Task Invalid_database_prerequisite_is_not_misreported_as_budget_failure(string provider, string key)
    {
        var settings = Settings(provider); settings["Database"]![key] = key == "CommandTimeoutSeconds" ? JsonValue.Create(0) : JsonValue.Create("budget-probe");
        using var fixture = new Workspace(settings.ToJsonString());
        var result = await Diagnose(fixture, "production");
        Assert.AreEqual(1, result.ExitCode);
        var code = key == "Provider" ? "DIAG_DATABASE_PROVIDER_INVALID" : key == "CommandTimeoutSeconds" ? "DIAG_DATABASE_TIMEOUT_INVALID" : "DIAG_DATABASE_GUID_STORAGE_INVALID";
        StringAssert.Contains(result.Output, code + " error");
        Assert.IsFalse(result.Output.Contains("code_generation.database_capacity.", StringComparison.Ordinal));
    }

    private static JsonObject Settings(string provider) => new()
    {
        ["Database"] = new JsonObject { ["Provider"] = provider, ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionString"] = "Server=example.invalid;Password=budget-probe;Max Pool Size=10" },
        ["DatabaseCapacity"] = new JsonObject { ["Enabled"] = true, ["HostRole"] = "Api", ["PermitLimit"] = 5, ["QueueLimit"] = 2, ["AcquireTimeoutMilliseconds"] = 250, ["ExpectedMaxPoolSize"] = 10, ["HealthReserve"] = 1, ["CriticalWorkerReserve"] = 1, ["ApiMaxReplicas"] = 2, ["ApiMaxPoolSize"] = 10, ["WorkerMaxReplicas"] = 1, ["WorkerMaxPoolSize"] = 10, ["MigrationReserve"] = 2, ["TotalBudget"] = 32 },
        ["FullNet"] = new JsonObject { ["Modules"] = new JsonObject { ["Preset"] = "minimal" } },
        ["Identity"] = new JsonObject { ["EnableTokenEndpoints"] = false, ["Oidc"] = new JsonObject { ["Enable"] = false } },
    };

    private static bool RuntimeValid(string configuration, string? overlay, string profile)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        builder.AddEnvironmentVariables();
        using var runtime = new ServiceCollection().AddFullNetDapper(builder.Build(), profile == "production" ? "Production" : "Development").BuildServiceProvider();
        try
        {
            _ = runtime.GetRequiredService<IOptions<DatabaseCapacityOptions>>().Value;
            return true;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return false; }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, bool valid)
    {
        var result = await Diagnose(fixture, profile);
        Assert.AreEqual(valid ? 0 : 1, result.ExitCode, result.Output);
        if (valid) Assert.IsFalse(result.Output.Contains("code_generation.database_capacity.invalid", StringComparison.Ordinal));
        else StringAssert.Contains(result.Output, "code_generation.database_capacity.invalid error");
    }

    private static async Task<(int ExitCode, string Output)> Diagnose(Workspace fixture, string profile)
    {
        var before = fixture.Files.ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        Assert.IsFalse(text.Contains("budget-probe", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), fixture.Files);
        foreach (var pair in before) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key));
        return (exit, text);
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-budget-" + Guid.NewGuid().ToString("N"));
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
            var id = "fullnet-budget-" + Guid.NewGuid().ToString("N");
            var project = Path.Combine(Root, "src", "Demo.Host.Api");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Demo.Host.Api.csproj"), "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory); File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }
        public void Dispose() { Directory.Delete(Root, true); if (secretsDirectory is not null) Directory.Delete(secretsDirectory, true); }
    }
}
