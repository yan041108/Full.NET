using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseConnectionSyntaxTests
{
    private readonly Dictionary<string, string?> originalEnvironment = new(StringComparer.Ordinal);

    [TestInitialize]
    public void Isolate_connection_and_identity_configuration()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key?.ToString() is { } key && IsRelevantEnvironment(key))
            {
                originalEnvironment[key] = entry.Value?.ToString();
                Environment.SetEnvironmentVariable(key, null);
            }
    }

    [TestCleanup]
    public void Restore_environment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key?.ToString() is { } key && IsRelevantEnvironment(key))
                Environment.SetEnvironmentVariable(key, null);
        foreach (var pair in originalEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private static bool IsRelevantEnvironment(string key)
    {
        var path = key.Replace("__", ":", StringComparison.Ordinal);
        return new[] { "Database:", "ConnectionStrings:", "Identity:", "FullNet:Modules", "MYSQLCONNSTR_", "SQLCONNSTR_", "SQLAZURECONNSTR_", "CUSTOMCONNSTR_" }
            .Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("SqlServer", "development", "normal")]
    [DataRow("SqlServer", "development", "unknown-key")]
    [DataRow("SqlServer", "development", "unclosed-quote")]
    [DataRow("SqlServer", "development", "no-key-value")]
    [DataRow("SqlServer", "development", "invalid-pool")]
    [DataRow("SqlServer", "development", "invalid-pooling")]
    [DataRow("SqlServer", "development", "quoted-semicolon")]
    [DataRow("SqlServer", "development", "duplicate-repaired")]
    [DataRow("SqlServer", "development", "native-option")]
    [DataRow("SqlServer", "development", "foreign-option")]
    [DataRow("SqlServer", "production", "normal")]
    [DataRow("SqlServer", "production", "unknown-key")]
    [DataRow("SqlServer", "production", "unclosed-quote")]
    [DataRow("SqlServer", "production", "no-key-value")]
    [DataRow("SqlServer", "production", "invalid-pool")]
    [DataRow("SqlServer", "production", "invalid-pooling")]
    [DataRow("SqlServer", "production", "quoted-semicolon")]
    [DataRow("SqlServer", "production", "duplicate-repaired")]
    [DataRow("SqlServer", "production", "native-option")]
    [DataRow("SqlServer", "production", "foreign-option")]
    [DataRow("MySql", "development", "normal")]
    [DataRow("MySql", "development", "unknown-key")]
    [DataRow("MySql", "development", "unclosed-quote")]
    [DataRow("MySql", "development", "no-key-value")]
    [DataRow("MySql", "development", "invalid-pool")]
    [DataRow("MySql", "development", "invalid-pooling")]
    [DataRow("MySql", "development", "quoted-semicolon")]
    [DataRow("MySql", "development", "duplicate-repaired")]
    [DataRow("MySql", "development", "native-option")]
    [DataRow("MySql", "development", "foreign-option")]
    [DataRow("MySql", "production", "normal")]
    [DataRow("MySql", "production", "unknown-key")]
    [DataRow("MySql", "production", "unclosed-quote")]
    [DataRow("MySql", "production", "no-key-value")]
    [DataRow("MySql", "production", "invalid-pool")]
    [DataRow("MySql", "production", "invalid-pooling")]
    [DataRow("MySql", "production", "quoted-semicolon")]
    [DataRow("MySql", "production", "duplicate-repaired")]
    [DataRow("MySql", "production", "native-option")]
    [DataRow("MySql", "production", "foreign-option")]
    public async Task Selected_connection_syntax_matches_actual_unopened_factory(string provider, string profile, string shape)
    {
        var valid = shape is "normal" or "quoted-semicolon" or "native-option"
            || (shape == "duplicate-repaired" && provider == "SqlServer");
        using var fixture = new Workspace(Configuration(provider, Connection(provider, shape)));
        Assert.AreEqual(valid, RuntimeConnectionValid(fixture.Configuration, null, profile));
        await AssertDiagnostic(fixture, profile, valid);
    }

    [TestMethod]
    [DataRow("SqlServer", false, "profile", false)]
    [DataRow("SqlServer", false, "profile", true)]
    [DataRow("SqlServer", false, "secrets", false)]
    [DataRow("SqlServer", false, "secrets", true)]
    [DataRow("SqlServer", false, "environment", false)]
    [DataRow("SqlServer", false, "environment", true)]
    [DataRow("SqlServer", true, "profile", false)]
    [DataRow("SqlServer", true, "profile", true)]
    [DataRow("SqlServer", true, "secrets", false)]
    [DataRow("SqlServer", true, "secrets", true)]
    [DataRow("SqlServer", true, "environment", false)]
    [DataRow("SqlServer", true, "environment", true)]
    [DataRow("MySql", false, "profile", false)]
    [DataRow("MySql", false, "profile", true)]
    [DataRow("MySql", false, "secrets", false)]
    [DataRow("MySql", false, "secrets", true)]
    [DataRow("MySql", false, "environment", false)]
    [DataRow("MySql", false, "environment", true)]
    [DataRow("MySql", true, "profile", false)]
    [DataRow("MySql", true, "profile", true)]
    [DataRow("MySql", true, "secrets", false)]
    [DataRow("MySql", true, "secrets", true)]
    [DataRow("MySql", true, "environment", false)]
    [DataRow("MySql", true, "environment", true)]
    public async Task Final_selected_connection_uses_profile_secrets_or_environment(string provider, bool named, string layer, bool valid)
    {
        var key = named ? "ConnectionStrings:selected" : "Database:ConnectionString";
        using var fixture = new Workspace(Configuration(provider, Connection(provider, valid ? "unknown-key" : "normal"), named));
        var overlay = new JsonObject { [key] = Connection(provider, valid ? "normal" : "unknown-key") }.ToJsonString();
        if (layer == "profile") fixture.WriteProfile(overlay);
        if (layer == "secrets") fixture.WriteSecrets(overlay);
        if (layer == "environment") Environment.SetEnvironmentVariable(key.Replace(":", "__"), Connection(provider, valid ? "normal" : "unknown-key"));
        Assert.AreEqual(valid, RuntimeConnectionValid(fixture.Configuration, layer == "environment" ? null : overlay, "development"));
        await AssertDiagnostic(fixture, "development", valid);
    }

    [TestMethod]
    [DataRow("SqlServer", false)]
    [DataRow("SqlServer", true)]
    [DataRow("MySql", false)]
    [DataRow("MySql", true)]
    public async Task Production_ignores_development_secret_repair(string provider, bool named)
    {
        using var fixture = new Workspace(Configuration(provider, Connection(provider, "unknown-key"), named));
        fixture.WriteSecrets(new JsonObject { [named ? "ConnectionStrings:selected" : "Database:ConnectionString"] = Connection(provider, "normal") }.ToJsonString());
        Assert.IsFalse(RuntimeConnectionValid(fixture.Configuration, null, "production"));
        await AssertDiagnostic(fixture, "production", false);
    }

    [TestMethod]
    [DataRow("SqlServer", "development")]
    [DataRow("SqlServer", "production")]
    [DataRow("MySql", "development")]
    [DataRow("MySql", "production")]
    public async Task Invalid_nonempty_direct_connection_is_not_masked_by_valid_named_connection(string provider, string profile)
    {
        var settings = JsonNode.Parse(Configuration(provider, Connection(provider, "unknown-key")))!.AsObject();
        settings["ConnectionStrings"] = new JsonObject { ["selected"] = Connection(provider, "normal") };
        using var fixture = new Workspace(settings.ToJsonString());
        Assert.IsFalse(RuntimeConnectionValid(fixture.Configuration, null, profile));
        await AssertDiagnostic(fixture, profile, false);
    }

    [TestMethod]
    [DataRow("SqlServer", false, "null")]
    [DataRow("SqlServer", false, "{}")]
    [DataRow("SqlServer", true, "null")]
    [DataRow("SqlServer", true, "{}")]
    [DataRow("MySql", false, "null")]
    [DataRow("MySql", false, "{}")]
    [DataRow("MySql", true, "null")]
    [DataRow("MySql", true, "{}")]
    public async Task Empty_parent_keeps_lower_selected_connection(string provider, bool named, string parent)
    {
        using var fixture = new Workspace(Configuration(provider, Connection(provider, "unknown-key"), named));
        var overlay = new JsonObject { [named ? "ConnectionStrings" : "Database"] = JsonNode.Parse(parent) }.ToJsonString();
        fixture.WriteProfile(overlay);
        Assert.IsFalse(RuntimeConnectionValid(fixture.Configuration, overlay, "development"));
        await AssertDiagnostic(fixture, "development", false);
    }

    [TestMethod]
    [DataRow("SqlServer", "development")]
    [DataRow("SqlServer", "production")]
    [DataRow("MySql", "development")]
    [DataRow("MySql", "production")]
    public async Task Final_provider_selects_the_matching_parser(string provider, string profile)
    {
        var other = provider == "SqlServer" ? "MySql" : "SqlServer";
        using var fixture = new Workspace(Configuration(other, Connection(provider, "native-option")));
        var overlay = new JsonObject { ["Database:Provider"] = provider }.ToJsonString();
        fixture.WriteProfile(overlay, profile);
        Assert.IsTrue(RuntimeConnectionValid(fixture.Configuration, overlay, profile));
        await AssertDiagnostic(fixture, profile, true);
    }

    [TestMethod]
    [DataRow("SqlServer", "development")]
    [DataRow("SqlServer", "production")]
    [DataRow("MySql", "development")]
    [DataRow("MySql", "production")]
    public async Task Unused_named_connection_is_not_parsed(string provider, string profile)
    {
        var settings = JsonNode.Parse(Configuration(provider, Connection(provider, "normal")))!.AsObject();
        settings["ConnectionStrings"] = new JsonObject { ["selected"] = Connection(provider, "unknown-key"), ["unused"] = "connection-probe" };
        using var fixture = new Workspace(settings.ToJsonString());
        Assert.IsTrue(RuntimeConnectionValid(fixture.Configuration, null, profile));
        await AssertDiagnostic(fixture, profile, true);
    }

    [TestMethod]
    [DataRow("development", "LegacyChar36", "omitted")]
    [DataRow("development", "LegacyChar36", "Default")]
    [DataRow("development", "LegacyChar36", "Binary16")]
    [DataRow("development", "LegacyChar36", "Char36")]
    [DataRow("development", "LegacyChar36", "Char32")]
    [DataRow("development", "LegacyChar36", "TimeSwapBinary16")]
    [DataRow("development", "LegacyChar36", "LittleEndianBinary16")]
    [DataRow("development", "LegacyChar36", "None")]
    [DataRow("development", "LegacyChar36", "old-false")]
    [DataRow("development", "LegacyChar36", "old-true")]
    [DataRow("development", "Binary16", "omitted")]
    [DataRow("development", "Binary16", "Default")]
    [DataRow("development", "Binary16", "Binary16")]
    [DataRow("development", "Binary16", "Char36")]
    [DataRow("development", "Binary16", "Char32")]
    [DataRow("development", "Binary16", "TimeSwapBinary16")]
    [DataRow("development", "Binary16", "LittleEndianBinary16")]
    [DataRow("development", "Binary16", "None")]
    [DataRow("development", "Binary16", "old-false")]
    [DataRow("development", "Binary16", "old-true")]
    [DataRow("production", "Binary16", "omitted")]
    [DataRow("production", "Binary16", "Default")]
    [DataRow("production", "Binary16", "Binary16")]
    [DataRow("production", "Binary16", "Char36")]
    [DataRow("production", "Binary16", "Char32")]
    [DataRow("production", "Binary16", "TimeSwapBinary16")]
    [DataRow("production", "Binary16", "LittleEndianBinary16")]
    [DataRow("production", "Binary16", "None")]
    [DataRow("production", "Binary16", "old-false")]
    [DataRow("production", "Binary16", "old-true")]
    public async Task Selected_mysql_guid_options_match_actual_unopened_factory(string profile, string mode, string shape)
    {
        var valid = shape == "omitted" || shape == (mode == "LegacyChar36" ? "Default" : "Binary16");
        var option = shape switch
        {
            "omitted" => "",
            "old-false" => ";Old Guids=false",
            "old-true" => ";oldguids=true",
            _ => ";gUiD fOrMaT=" + shape,
        };
        var settings = JsonNode.Parse(Configuration("MySql", Connection("MySql", "normal") + option))!.AsObject();
        settings["Database"]!["MySqlGuidStorageMode"] = mode;
        using var fixture = new Workspace(settings.ToJsonString());
        Assert.AreEqual(valid, RuntimeConnectionValid(fixture.Configuration, null, profile), "先核对真实工厂的 UUID 策略，连接保持未打开。");
        await AssertDiagnostic(fixture, profile, valid);
    }

    [TestMethod]
    [DataRow(false, "profile", "LegacyChar36")]
    [DataRow(false, "profile", "Binary16")]
    [DataRow(false, "secrets", "LegacyChar36")]
    [DataRow(false, "secrets", "Binary16")]
    [DataRow(false, "environment", "LegacyChar36")]
    [DataRow(false, "environment", "Binary16")]
    [DataRow(true, "profile", "LegacyChar36")]
    [DataRow(true, "profile", "Binary16")]
    [DataRow(true, "secrets", "LegacyChar36")]
    [DataRow(true, "secrets", "Binary16")]
    [DataRow(true, "environment", "LegacyChar36")]
    [DataRow(true, "environment", "Binary16")]
    public async Task Mysql_guid_policy_uses_effective_storage_mode(bool named, string layer, string mode)
    {
        var settings = JsonNode.Parse(Configuration("MySql", Connection("MySql", "normal") + ";GuidFormat=Binary16", named))!.AsObject();
        settings["Database"]!["MySqlGuidStorageMode"] = mode == "Binary16" ? "LegacyChar36" : "Binary16";
        using var fixture = new Workspace(settings.ToJsonString());
        var overlay = new JsonObject { ["dAtAbAsE:MySqlGuidStorageMode"] = mode }.ToJsonString();
        if (layer == "profile") fixture.WriteProfile(overlay);
        if (layer == "secrets") fixture.WriteSecrets(overlay);
        if (layer == "environment") Environment.SetEnvironmentVariable("Database__MySqlGuidStorageMode", mode);
        var valid = mode == "Binary16";
        Assert.AreEqual(valid, RuntimeConnectionValid(fixture.Configuration, layer == "environment" ? null : overlay, "development"));
        await AssertDiagnostic(fixture, "development", valid);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Production_mysql_guid_policy_ignores_development_secret_repair(bool named)
    {
        using var fixture = new Workspace(Configuration("MySql", Connection("MySql", "normal") + ";GuidFormat=Default", named));
        fixture.WriteSecrets(new JsonObject { ["Database:MySqlGuidStorageMode"] = "LegacyChar36" }.ToJsonString());
        Assert.IsFalse(RuntimeConnectionValid(fixture.Configuration, null, "production"));
        await AssertDiagnostic(fixture, "production", false);
    }

    private static string Connection(string provider, string shape) => shape switch
    {
        "normal" => "Server=example.invalid;Password=connection-probe",
        "unknown-key" => "Server=example.invalid;Password=connection-probe;UnknownConnectionProbe=connection-probe",
        "unclosed-quote" => "Server=example.invalid;Password=\"connection-probe",
        "no-key-value" => "connection-probe",
        "invalid-pool" => "Server=example.invalid;Password=connection-probe;Max Pool Size=oops",
        "invalid-pooling" => "Server=example.invalid;Password=connection-probe;Pooling=oops",
        "quoted-semicolon" => "Server=example.invalid;Password=\"connection;probe\"",
        "duplicate-repaired" => "Server=example.invalid;Pooling=oops;Pooling=true;Password=connection-probe",
        "native-option" => provider == "SqlServer" ? "Data Source=example.invalid;Integrated Security=true;Encrypt=Strict" : "Server=example.invalid;Password=connection-probe;SslMode=Required;Allow User Variables=false",
        "foreign-option" => provider == "SqlServer" ? "Server=example.invalid;Password=connection-probe;SslMode=Required" : "Server=example.invalid;Password=connection-probe;Integrated Security=true;Encrypt=Strict",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static string Configuration(string provider, string connection, bool named = false) => new JsonObject
    {
        ["Database"] = new JsonObject { ["Provider"] = provider, ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionName"] = "selected", ["ConnectionString"] = named ? "" : connection },
        ["ConnectionStrings"] = new JsonObject { ["selected"] = named ? connection : "" },
        ["FullNet"] = new JsonObject { ["Modules"] = new JsonObject { ["Preset"] = "minimal" } },
        ["Identity"] = new JsonObject { ["EnableTokenEndpoints"] = false, ["Oidc"] = new JsonObject { ["Enable"] = false } },
    }.ToJsonString();

    private static bool RuntimeConnectionValid(string configuration, string? overlay, string profile)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        builder.AddEnvironmentVariables();
        var services = new ServiceCollection();
        services.AddFullNetDapper(builder.Build(), profile == "production" ? "Production" : "Development");
        using var runtime = services.BuildServiceProvider();
        try
        {
            // 只创建真实工厂的未打开连接，不访问网络；MySQL 经过既有连接策略。
            using var connection = runtime.GetRequiredService<IDbConnectionFactory>().Create();
            Assert.AreEqual(ConnectionState.Closed, connection.State);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, bool valid)
    {
        var before = fixture.Files.ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        StringAssert.Contains(text, valid ? "DIAG_CONNECTION_CONFIGURED ok" : "DIAG_CONNECTION_INVALID error");
        Assert.AreEqual(valid ? 0 : 1, result, text);
        Assert.AreEqual(valid, text.Contains("DIAG_CONNECTION_CONFIGURED ok", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("connection-probe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("connection;probe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("UnknownConnectionProbe", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), fixture.Files);
        foreach (var pair in before) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key));
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-connection-syntax-" + Guid.NewGuid().ToString("N"));
        public string Configuration { get; }
        private string? secretsDirectory;
        public string[] Files => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Concat(secretsDirectory is null ? [] : Directory.EnumerateFiles(secretsDirectory)).ToArray();
        public Workspace(string configuration)
        {
            Directory.CreateDirectory(Root); Configuration = configuration;
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), configuration);
            File.WriteAllText(Path.Combine(Root, "global.json"), "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"latestFeature\",\"allowPrerelease\":true}}");
        }
        public void WriteProfile(string content, string profile = "development") => File.WriteAllText(Path.Combine(Root, "appsettings." + (profile == "production" ? "Production" : "Development") + ".json"), content);
        public void WriteSecrets(string content)
        {
            var id = "fullnet-connection-syntax-" + Guid.NewGuid().ToString("N");
            var project = Path.Combine(Root, "src", "Demo.Host.Api");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Demo.Host.Api.csproj"), "<Project><PropertyGroup><UserSecretsId>" + id + "</UserSecretsId></PropertyGroup></Project>");
            secretsDirectory = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(secretsDirectory); File.WriteAllText(Path.Combine(secretsDirectory, "secrets.json"), content);
        }
        public void Dispose() { Directory.Delete(Root, true); if (secretsDirectory is not null) Directory.Delete(secretsDirectory, true); }
    }
}
