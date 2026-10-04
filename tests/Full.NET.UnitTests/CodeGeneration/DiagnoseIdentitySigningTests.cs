using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseIdentitySigningTests
{
    private Dictionary<string, string?> environment = [];

    [TestInitialize]
    public void Isolate_identity_environment()
    {
        environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key.ToString()!.Replace("__", ":").StartsWith("Identity:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString());
        foreach (var key in environment.Keys) Environment.SetEnvironmentVariable(key, null);
    }

    [TestCleanup]
    public void Restore_identity_environment()
    {
        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key.ToString()!.Replace("__", ":").StartsWith("Identity:", StringComparison.OrdinalIgnoreCase)).ToArray())
            Environment.SetEnvironmentVariable(entry.Key.ToString()!, null);
        foreach (var entry in environment) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
    }

    [TestMethod]
    [DataRow("{}", "development", false, "DIAG_IDENTITY_SIGNING_REQUIRED warn")]
    [DataRow("{}", "production", false, "DIAG_IDENTITY_SIGNING_REQUIRED error")]
    [DataRow("""{"EnableTokenEndpoints":false}""", "production", true, "DIAG_IDENTITY_TOKEN_ENDPOINTS_DISABLED ok")]
    [DataRow("""{"AllowDevelopmentEphemeralSigningKey":true}""", "development", true, "DIAG_IDENTITY_EPHEMERAL_SIGNING warn")]
    [DataRow("""{"AllowDevelopmentEphemeralSigningKey":true}""", "production", false, "DIAG_IDENTITY_EPHEMERAL_SIGNING error")]
    [DataRow("""{"EnableTokenEndpoints":false,"AllowDevelopmentEphemeralSigningKey":true}""", "production", false, "DIAG_IDENTITY_EPHEMERAL_SIGNING error")]
    [DataRow("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""", "production", true, "DIAG_IDENTITY_SIGNING_CONFIGURED ok")]
    [DataRow("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe"}}}""", "production", false, "DIAG_IDENTITY_SIGNING_REQUIRED error")]
    [DataRow("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PrivateKeyPem":"private-signing-probe"}}}""", "development", false, "DIAG_IDENTITY_SIGNING_REQUIRED warn")]
    [DataRow("""{"ActiveKeyId":"SIGNING-PROBE","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""", "production", false, "DIAG_IDENTITY_SIGNING_REQUIRED error")]
    [DataRow("""{"ActiveKeyId":"signing-probe","SigningKeys":{"SIGNING-PROBE":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""", "production", false, "DIAG_IDENTITY_SIGNING_REQUIRED error")]
    public async Task Signing_prerequisites_match_real_identity_validator(
        string identity, string profile, bool runtimeValid, string finding)
    {
        using var fixture = new Workspace(identity);
        Assert.AreEqual(runtimeValid, RuntimeValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, finding);
    }

    [TestMethod]
    [DataRow("base", "development")]
    [DataRow("profile", "development")]
    [DataRow("secrets", "development")]
    [DataRow("environment", "development")]
    [DataRow("base", "production")]
    [DataRow("profile", "production")]
    [DataRow("environment", "production")]
    public async Task Signing_key_leaves_follow_host_configuration_sources(string source, string profile)
    {
        const string identity = """{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""";
        using var fixture = new Workspace(source == "base" ? identity : "{}");
        var overlay = """{"identity:activekeyid":"signing-probe","IDENTITY:SIGNINGKEYS:signing-probe:PublicKeyPem":"public-signing-probe","identity:signingkeys:signing-probe:privatekeypem":"private-signing-probe"}""";
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, string>>(overlay)!)
                Environment.SetEnvironmentVariable(entry.Key.Replace(":", "__"), entry.Value);
        Assert.IsTrue(RuntimeValid(fixture.Configuration, profile,
            source == "base" ? null : overlay));
        await AssertDiagnostic(fixture, profile, "DIAG_IDENTITY_SIGNING_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("EnableTokenEndpoints", "42")]
    [DataRow("EnableTokenEndpoints", "\"signing-probe\"")]
    [DataRow("EnableTokenEndpoints", "\"\"")]
    [DataRow("AllowDevelopmentEphemeralSigningKey", "42")]
    [DataRow("AllowDevelopmentEphemeralSigningKey", "\"signing-probe\"")]
    [DataRow("AllowDevelopmentEphemeralSigningKey", "\"\"")]
    public async Task Unbindable_boolean_configuration_is_an_error(string field, string value)
    {
        using var fixture = new Workspace("{\"" + field + "\":" + value + "}");
        Assert.IsFalse(RuntimeValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", "DIAG_IDENTITY_SIGNING_OPTIONS_INVALID error");
    }

    [TestMethod]
    public async Task Environment_override_cannot_be_hidden_by_base_key_or_development_secrets()
    {
        using var fixture = new Workspace("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""");
        fixture.WriteSecrets("""{"Identity:SigningKeys:signing-probe:PrivateKeyPem":"secret-signing-probe"}""");
        Environment.SetEnvironmentVariable("Identity__SigningKeys__signing-probe__PrivateKeyPem", "");
        await AssertDiagnostic(fixture, "development", "DIAG_IDENTITY_SIGNING_REQUIRED warn");
    }

    [TestMethod]
    public async Task Production_ignores_development_secrets()
    {
        using var fixture = new Workspace("{}");
        fixture.WriteSecrets("""{"Identity:AllowDevelopmentEphemeralSigningKey":true}""");
        await AssertDiagnostic(fixture, "production", "DIAG_IDENTITY_SIGNING_REQUIRED error");
    }

    [TestMethod]
    public async Task Placeholder_key_is_not_reported_as_configured()
    {
        using var fixture = new Workspace("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"YOUR_public-signing-probe","PrivateKeyPem":"CHANGEME-private-signing-probe"}}}""");
        // 宿主 Options 只校验非空，CLI 额外识别模板占位符；两者都未验证 PEM 密码学有效性。
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", "DIAG_IDENTITY_SIGNING_REQUIRED error");
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    public async Task Explicit_null_boolean_follows_real_binding_instead_of_restoring_default(string value)
    {
        using var fixture = new Workspace("{\"EnableTokenEndpoints\":" + value + "}");
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", "DIAG_IDENTITY_TOKEN_ENDPOINTS_DISABLED ok");
    }

    [TestMethod]
    [DataRow("profile", "development", false)]
    [DataRow("profile", "production", false)]
    [DataRow("secrets", "development", false)]
    [DataRow("environment", "development", false)]
    [DataRow("profile", "development", true)]
    [DataRow("secrets", "development", true)]
    [DataRow("environment", "production", true)]
    public async Task Higher_priority_key_spelling_and_empty_parent_match_real_binding(
        string source, string profile, bool emptyParent)
    {
        using var fixture = new Workspace("""{"ActiveKeyId":"signing-probe","SigningKeys":{"signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""");
        var overlay = emptyParent ? """{"Identity:SigningKeys":{}}"""
            : """{"Identity:SigningKeys:SIGNING-PROBE:PrivateKeyPem":"override-signing-probe"}""";
        Assert.AreEqual(emptyParent, RuntimeValid(fixture.Configuration, profile, overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            Environment.SetEnvironmentVariable(emptyParent ? "Identity__SigningKeys" : "Identity__SigningKeys__SIGNING-PROBE__PrivateKeyPem",
                emptyParent ? "" : "override-signing-probe");
        await AssertDiagnostic(fixture, profile, emptyParent ? "DIAG_IDENTITY_SIGNING_CONFIGURED ok"
            : "DIAG_IDENTITY_SIGNING_REQUIRED " + (profile == "production" ? "error" : "warn"));
    }

    private static bool RuntimeValid(string configuration, string profile, string? overlay = null)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(profile == "development" ? Environments.Development : Environments.Production);
        try
        {
            var options = new IdentityOptions();
            builder.Build().GetSection("Identity").Bind(options);
            return new IdentityOptionsValidator(host).Validate(null, options).Succeeded;
        }
        catch (InvalidOperationException) { return false; }
    }

    private static async Task AssertDiagnostic(Workspace fixture, string profile, string finding)
    {
        var before = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, finding);
        Assert.AreEqual(finding.EndsWith(" error", StringComparison.Ordinal) ? 1 : 0, result);
        Assert.IsFalse(text.Contains("signing-probe", StringComparison.Ordinal));
        Assert.AreEqual(before.Count, Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).Count());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, File.ReadAllBytes(entry.Key));
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-diagnose-signing-" + Guid.NewGuid().ToString("N"));
        public string Configuration { get; }
        private string? secretsDirectory;

        public Workspace(string identity)
        {
            Directory.CreateDirectory(Root);
            Configuration = """{"Database":{"Provider":"SqlServer","MySqlGuidStorageMode":"Binary16","ConnectionString":"Server=example.invalid;Password=connection-probe"},"FullNet":{"Modules":{"Preset":"minimal"}},"Identity":""" + identity + "}";
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), Configuration);
            File.WriteAllText(Path.Combine(Root, "global.json"), """{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":true}}""");
        }

        public void WriteProfile(string profile, string content) => File.WriteAllText(
            Path.Combine(Root, "appsettings." + (profile == "development" ? "Development" : "Production") + ".json"), content);

        public void WriteSecrets(string content)
        {
            var id = "fullnet-signing-" + Guid.NewGuid().ToString("N");
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
