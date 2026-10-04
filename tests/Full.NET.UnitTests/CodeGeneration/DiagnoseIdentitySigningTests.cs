using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [TestMethod]
    [DataRow("null", "disabled", "production")]
    [DataRow("{}", "disabled", "development")]
    [DataRow("""{"inactive-signing-probe":null}""", "disabled", "development")]
    [DataRow("""{"inactive-signing-probe":{}}""", "disabled", "production")]
    [DataRow("null", "ephemeral", "development")]
    [DataRow("{}", "ephemeral", "development")]
    [DataRow("""{"inactive-signing-probe":null}""", "ephemeral", "development")]
    [DataRow("""{"inactive-signing-probe":{}}""", "ephemeral", "development")]
    [DataRow("null", "configured", "production")]
    [DataRow("{}", "configured", "production")]
    [DataRow("""{"inactive-signing-probe":null}""", "configured", "production")]
    [DataRow("""{"inactive-signing-probe":{}}""", "configured", "production")]
    public async Task Null_signing_dictionary_or_entry_follows_actual_configuration_binding(
        string keys, string mode, string profile)
    {
        var prefix = mode switch
        {
            "disabled" => "\"EnableTokenEndpoints\":false",
            "ephemeral" => "\"AllowDevelopmentEphemeralSigningKey\":true",
            _ => "\"ActiveKeyId\":\"signing-probe\"",
        };
        if (mode == "configured" && keys is not ("null" or "{}"))
            keys = keys[..^1] + ""","signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}""";
        using var fixture = new Workspace("{" + prefix + ",\"SigningKeys\":" + keys + "}");
        // 手工 Options 的 null 集合校验不能直接用于 JSON；绑定器保留已初始化字典并跳过空条目。
        var missingActiveKey = mode == "configured" && keys is "null" or "{}";
        Assert.AreEqual(!missingActiveKey, RuntimeValid(fixture.Configuration, profile));
        var finding = mode switch
        {
            "disabled" => "DIAG_IDENTITY_TOKEN_ENDPOINTS_DISABLED ok",
            "ephemeral" => "DIAG_IDENTITY_EPHEMERAL_SIGNING warn",
            _ => missingActiveKey ? "DIAG_IDENTITY_SIGNING_REQUIRED error" : "DIAG_IDENTITY_SIGNING_CONFIGURED ok",
        };
        await AssertDiagnostic(fixture, profile, finding);
    }

    [TestMethod]
    [DataRow("profile", "development", "null")]
    [DataRow("profile", "production", "{}")]
    [DataRow("secrets", "development", "null")]
    [DataRow("secrets", "development", "{}")]
    public async Task Higher_priority_null_entry_does_not_remove_lower_key_children(
        string source, string profile, string value)
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false,"SigningKeys":{"inactive-signing-probe":{"PublicKeyPem":"public-signing-probe"}}}""");
        var overlay = "{\"Identity:SigningKeys:inactive-signing-probe\":" + value + "}";
        Assert.IsTrue(RuntimeValid(fixture.Configuration, profile, overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        else fixture.WriteSecrets(overlay);
        await AssertDiagnostic(fixture, profile, "DIAG_IDENTITY_TOKEN_ENDPOINTS_DISABLED ok");
    }

    [TestMethod]
    [DataRow("{}", "development", false, "DIAG_OIDC_SIGNING_REQUIRED warn")]
    [DataRow("{}", "production", false, "DIAG_OIDC_SIGNING_REQUIRED error")]
    [DataRow("""{"Enable":false}""", "production", true, "DIAG_OIDC_DISABLED ok")]
    [DataRow("""{"Enable":null}""", "production", true, "DIAG_OIDC_DISABLED ok")]
    [DataRow("""{"Enable":false,"AllowDevelopmentEphemeralSigningKey":true}""", "production", true, "DIAG_OIDC_DISABLED ok")]
    [DataRow("""{"AllowDevelopmentEphemeralSigningKey":true}""", "development", true, "DIAG_OIDC_EPHEMERAL_SIGNING warn")]
    [DataRow("""{"AllowDevelopmentEphemeralSigningKey":true}""", "production", false, "DIAG_OIDC_EPHEMERAL_SIGNING error")]
    [DataRow("""{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""", "production", true, "DIAG_OIDC_SIGNING_CONFIGURED ok")]
    [DataRow("""{"ActiveSigningKeyId":"OIDC-SIGNING-PROBE","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""", "production", false, "DIAG_OIDC_SIGNING_REQUIRED error")]
    [DataRow("""{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe"}}}""", "production", false, "DIAG_OIDC_SIGNING_REQUIRED error")]
    [DataRow("""{"Enable":"signing-probe"}""", "development", false, "DIAG_OIDC_SIGNING_OPTIONS_INVALID error")]
    [DataRow("""{"Enable":false,"AllowDevelopmentEphemeralSigningKey":42}""", "development", false, "DIAG_OIDC_SIGNING_OPTIONS_INVALID error")]
    public async Task Oidc_signing_prerequisites_match_its_separate_runtime_validator(
        string fragment, string profile, bool valid, string finding)
    {
        using var fixture = new Workspace(OidcIdentity(fragment));
        Assert.AreEqual(valid, RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, finding);
    }

    [TestMethod]
    public async Task Jwt_development_ephemeral_key_does_not_satisfy_enabled_oidc()
    {
        var identity = JsonNode.Parse(OidcIdentity("{}"))!.AsObject();
        identity["AllowDevelopmentEphemeralSigningKey"] = true;
        using var fixture = new Workspace(identity.ToJsonString());
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "development"));
        Assert.IsFalse(RuntimeOidcValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", "DIAG_OIDC_SIGNING_REQUIRED warn");
    }

    [TestMethod]
    [DataRow("base", "development")]
    [DataRow("profile", "development")]
    [DataRow("secrets", "development")]
    [DataRow("environment", "development")]
    [DataRow("base", "production")]
    [DataRow("profile", "production")]
    [DataRow("environment", "production")]
    public async Task Oidc_signing_values_follow_all_supported_configuration_sources(string source, string profile)
    {
        const string configured = """{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}""";
        using var fixture = new Workspace(OidcIdentity(source == "base" ? configured : """{"Enable":false}"""));
        const string overlay = """{"identity:oidc:enable":true,"Identity:Oidc:ActiveSigningKeyId":"oidc-signing-probe","IDENTITY:OIDC:SIGNINGKEYS:oidc-signing-probe:PublicKeyPem":"public-signing-probe","identity:oidc:signingkeys:oidc-signing-probe:privatekeypem":"private-signing-probe"}""";
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            foreach (var entry in JsonNode.Parse(overlay)!.AsObject())
                Environment.SetEnvironmentVariable(entry.Key.Replace(":", "__"), entry.Value!.ToString());
        await AssertDiagnostic(fixture, profile, "DIAG_OIDC_SIGNING_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("secrets")]
    [DataRow("environment")]
    public async Task Oidc_higher_priority_key_spelling_must_still_match_active_id_exactly(string source)
    {
        using var fixture = new Workspace(OidcIdentity("""{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}"""));
        const string overlay = """{"Identity:Oidc:SigningKeys:OIDC-SIGNING-PROBE:PrivateKeyPem":"override-signing-probe"}""";
        Assert.IsFalse(RuntimeOidcValid(fixture.Configuration, "development", overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            Environment.SetEnvironmentVariable("Identity__Oidc__SigningKeys__OIDC-SIGNING-PROBE__PrivateKeyPem", "override-signing-probe");
        await AssertDiagnostic(fixture, "development", "DIAG_OIDC_SIGNING_REQUIRED warn");
    }

    [TestMethod]
    public async Task Oidc_production_does_not_read_development_user_secrets()
    {
        using var fixture = new Workspace(OidcIdentity("{}"));
        fixture.WriteSecrets("""{"Identity:Oidc:AllowDevelopmentEphemeralSigningKey":true}""");
        Assert.IsFalse(RuntimeOidcValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", "DIAG_OIDC_SIGNING_REQUIRED error");
    }

    [TestMethod]
    public async Task Oidc_placeholder_key_cannot_be_reported_as_configured()
    {
        using var fixture = new Workspace(OidcIdentity("""{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"YOUR_public-signing-probe","PrivateKeyPem":"CHANGEME_private-signing-probe"}}}"""));
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", "DIAG_OIDC_SIGNING_REQUIRED error");
    }

    [TestMethod]
    [DataRow(false, true, "True", true)]
    [DataRow(false, true, "true", false)]
    [DataRow(false, false, "False", true)]
    [DataRow(false, false, "false", false)]
    [DataRow(true, true, "True", true)]
    [DataRow(true, true, "true", false)]
    [DataRow(true, false, "False", true)]
    [DataRow(true, false, "false", false)]
    public async Task Boolean_key_id_matches_json_provider_scalar_text(bool oidc, bool active, string key, bool valid)
    {
        var fragment = new JsonObject
        {
            [oidc ? "ActiveSigningKeyId" : "ActiveKeyId"] = active,
            ["SigningKeys"] = new JsonObject
            {
                [key] = new JsonObject
                {
                    ["PublicKeyPem"] = "public-signing-probe",
                    ["PrivateKeyPem"] = "private-signing-probe"
                }
            }
        }.ToJsonString();
        using var fixture = new Workspace(oidc ? OidcIdentity(fragment) : fragment);
        Assert.AreEqual(valid, oidc ? RuntimeOidcValid(fixture.Configuration, "production") : RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", (oidc ? "DIAG_OIDC" : "DIAG_IDENTITY")
            + (valid ? "_SIGNING_CONFIGURED ok" : "_SIGNING_REQUIRED error"));
    }

    private static string OidcIdentity(string fragment)
    {
        var oidc = JsonNode.Parse("""{"Enable":true,"Issuer":"https://issuer.example.invalid","Clients":[{"ClientId":"client-signing-probe","RedirectUris":["https://client.example.invalid/callback"]}]}""")!.AsObject();
        foreach (var entry in JsonNode.Parse(fragment)!.AsObject()) oidc[entry.Key] = entry.Value?.DeepClone();
        return new JsonObject { ["EnableTokenEndpoints"] = false, ["Oidc"] = oidc }.ToJsonString();
    }

    private static bool RuntimeOidcValid(string configuration, string profile, string? overlay = null)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(profile == "development" ? Environments.Development : Environments.Production);
        try
        {
            var options = new IdentityOidcOptions();
            builder.Build().GetSection("Identity:Oidc").Bind(options);
            return new IdentityOidcOptionsValidator(host).Validate(null, options).Succeeded;
        }
        catch (InvalidOperationException) { return false; }
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
