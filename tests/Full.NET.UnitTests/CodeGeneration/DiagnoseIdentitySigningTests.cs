using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
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

    [TestMethod]
    [DataRow("null", "development", false)]
    [DataRow("\"\"", "production", false)]
    [DataRow("\" \"", "development", false)]
    [DataRow("false", "production", false)]
    [DataRow("42", "development", false)]
    [DataRow("\"/issuer-signing-probe\"", "production", false)]
    [DataRow("\"file:///issuer-signing-probe\"", "development", false)]
    [DataRow("\"https://user:private-signing-probe@issuer.example.invalid\"", "production", false)]
    [DataRow("\"https://issuer.example.invalid\"", "production", true)]
    [DataRow("\"http://localhost:5180\"", "development", true)]
    public async Task Oidc_issuer_preflight_matches_runtime_validator(string issuerJson, string profile, bool valid)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{\"Issuer\":" + issuerJson + "}"));
        Assert.AreEqual(valid, RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, valid ? "DIAG_OIDC_ISSUER_CONFIGURED ok" : "DIAG_OIDC_ISSUER_INVALID error");
    }

    [TestMethod]
    [DataRow("null", "development", true)]
    [DataRow("\"\"", "production", true)]
    [DataRow("\" \"", "development", true)]
    [DataRow("false", "production", false)]
    [DataRow("42", "development", false)]
    [DataRow("\"encryption-signing-probe\"", "production", false)]
    [DataRow("\"====\"", "development", false)]
    public async Task Oidc_optional_encryption_preflight_matches_runtime_validator(string keyJson, string profile, bool valid)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{\"EncryptionKeyBase64\":" + keyJson + "}"));
        Assert.AreEqual(valid, RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, valid ? "DIAG_OIDC_ISSUER_CONFIGURED ok" : "DIAG_OIDC_ENCRYPTION_INVALID error");
    }

    [TestMethod]
    [DataRow(31, false, "development", false)]
    [DataRow(32, false, "production", true)]
    [DataRow(32, true, "development", true)]
    [DataRow(33, false, "production", false)]
    public async Task Oidc_encryption_preflight_requires_exactly_256_bits(int bytes, bool whitespace, string profile, bool valid)
    {
        var key = SyntheticEncryptionKey(bytes);
        if (whitespace) key = key[..12] + "\r\n " + key[12..];
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { ["EncryptionKeyBase64"] = key }.ToJsonString()));
        Assert.AreEqual(valid, RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, valid ? "DIAG_OIDC_ENCRYPTION_CONFIGURED ok" : "DIAG_OIDC_ENCRYPTION_INVALID error", secret: key);
    }

    [TestMethod]
    [DataRow("Issuer", "base", "development")]
    [DataRow("Issuer", "profile", "development")]
    [DataRow("Issuer", "secrets", "development")]
    [DataRow("Issuer", "environment", "development")]
    [DataRow("Issuer", "base", "production")]
    [DataRow("Issuer", "profile", "production")]
    [DataRow("Issuer", "environment", "production")]
    [DataRow("EncryptionKeyBase64", "base", "development")]
    [DataRow("EncryptionKeyBase64", "profile", "development")]
    [DataRow("EncryptionKeyBase64", "secrets", "development")]
    [DataRow("EncryptionKeyBase64", "environment", "development")]
    [DataRow("EncryptionKeyBase64", "base", "production")]
    [DataRow("EncryptionKeyBase64", "profile", "production")]
    [DataRow("EncryptionKeyBase64", "environment", "production")]
    public async Task Oidc_non_signing_fields_follow_all_supported_sources(string field, string source, string profile)
    {
        var valid = field == "Issuer" ? "https://issuer.example.invalid" : SyntheticEncryptionKey(32);
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { [field] = source == "base" ? valid : "invalid-signing-probe" }.ToJsonString()));
        var path = ("Identity:Oidc:" + field).ToUpperInvariant();
        var overlay = new JsonObject { [path] = valid }.ToJsonString();
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment") Environment.SetEnvironmentVariable(path.Replace(":", "__"), valid);
        await AssertDiagnostic(fixture, profile, field == "Issuer" ? "DIAG_OIDC_ISSUER_CONFIGURED ok" : "DIAG_OIDC_ENCRYPTION_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("Issuer", "profile")]
    [DataRow("Issuer", "secrets")]
    [DataRow("Issuer", "environment")]
    [DataRow("EncryptionKeyBase64", "profile")]
    [DataRow("EncryptionKeyBase64", "secrets")]
    [DataRow("EncryptionKeyBase64", "environment")]
    public async Task Oidc_non_signing_empty_override_blocks_lower_layer_value(string field, string source)
    {
        var valid = field == "Issuer" ? "https://issuer.example.invalid" : SyntheticEncryptionKey(32);
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { [field] = valid }.ToJsonString()));
        var path = "Identity:Oidc:" + field;
        var overlay = new JsonObject { [path] = "" }.ToJsonString();
        Assert.AreEqual(field != "Issuer", RuntimeOidcValid(fixture.Configuration, "development", overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment") Environment.SetEnvironmentVariable(path.Replace(":", "__"), "");
        await AssertDiagnostic(fixture, "development",
            field == "Issuer" ? "DIAG_OIDC_ISSUER_INVALID error" : "DIAG_OIDC_ISSUER_CONFIGURED ok",
            field == "Issuer" ? null : "DIAG_OIDC_ENCRYPTION_");
    }

    [TestMethod]
    [DataRow("Issuer", "development")]
    [DataRow("Issuer", "production")]
    [DataRow("EncryptionKeyBase64", "development")]
    [DataRow("EncryptionKeyBase64", "production")]
    public async Task Disabled_oidc_does_not_require_issuer_or_encryption(string field, string profile)
    {
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject
        {
            ["Enable"] = false,
            [field] = "invalid-signing-probe"
        }.ToJsonString()));
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, "DIAG_OIDC_DISABLED ok", "DIAG_OIDC_ISSUER_");
        await AssertDiagnostic(fixture, profile, "DIAG_OIDC_DISABLED ok", "DIAG_OIDC_ENCRYPTION_");
    }

    [TestMethod]
    [DataRow("Issuer")]
    [DataRow("EncryptionKeyBase64")]
    public async Task Oidc_non_signing_production_ignores_development_secrets(string field)
    {
        var valid = field == "Issuer" ? "https://issuer.example.invalid" : SyntheticEncryptionKey(32);
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { [field] = valid }.ToJsonString()));
        fixture.WriteSecrets(new JsonObject { ["Identity:Oidc:" + field] = "invalid-signing-probe" }.ToJsonString());
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", field == "Issuer" ? "DIAG_OIDC_ISSUER_CONFIGURED ok" : "DIAG_OIDC_ENCRYPTION_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("false")]
    [DataRow("42")]
    [DataRow("\"client-signing-probe\"")]
    [DataRow("[null]")]
    [DataRow("[{}]")]
    [DataRow("[\"client-signing-probe\"]")]
    [DataRow("[{\"ClientId\":\"client-signing-probe\"}]")]
    [DataRow("[{\"RedirectUris\":[\"https://client.example.invalid/callback\"]}]")]
    [DataRow("[{\"ClientId\":\"client-signing-probe\",\"RedirectUris\":[\"https://client.example.invalid/callback\"]}]")]
    [DataRow("[null,{\"ClientId\":\"client-signing-probe\",\"RedirectUris\":[\"https://client.example.invalid/callback\"]}]")]
    [DataRow("[\"unused-signing-probe\",{\"ClientId\":\"client-signing-probe\",\"RedirectUris\":[\"https://client.example.invalid/callback\"]}]")]
    public async Task Oidc_client_collection_diagnosis_matches_real_binding_and_validation(string clients)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{\"Clients\":" + clients + "}"));
        // 集合中的 null、标量由真实 Binder 决定是否保留；不把 JSON 形状猜测当作宿主行为。
        var valid = RuntimeOidcValid(fixture.Configuration, "production");
        await AssertDiagnostic(fixture, "production", valid ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("RedirectUris", "null", false)]
    [DataRow("RedirectUris", "[]", false)]
    [DataRow("RedirectUris", "[null]", false)]
    [DataRow("RedirectUris", "[\"/callback-signing-probe\"]", false)]
    [DataRow("RedirectUris", "[\"file:///callback-signing-probe\"]", false)]
    [DataRow("RedirectUris", "[\"https://*.example.invalid/callback-signing-probe\"]", false)]
    [DataRow("RedirectUris", "[\"https://user:private-signing-probe@client.example.invalid/callback\"]", false)]
    [DataRow("RedirectUris", "[\"https://client.example.invalid/callback#fragment-signing-probe\"]", false)]
    [DataRow("RedirectUris", "[\"https://client.example.invalid/callback\",\"https://client.example.invalid/callback/\"]", false)]
    [DataRow("RedirectUris", "[\"http://localhost:25212/callback?code=signing-probe\"]", true)]
    [DataRow("PostLogoutRedirectUris", "null", true)]
    [DataRow("PostLogoutRedirectUris", "[]", true)]
    [DataRow("PostLogoutRedirectUris", "[\"https://client.example.invalid/logout\"]", true)]
    [DataRow("PostLogoutRedirectUris", "[\"https://client.example.invalid/logout#fragment-signing-probe\"]", false)]
    [DataRow("PostLogoutRedirectUris", "[\"https://client.example.invalid/logout\",\"https://client.example.invalid/logout/\"]", false)]
    [DataRow("ClientId", "null", false)]
    [DataRow("ClientId", "\" \"", false)]
    [DataRow("ClientId", "42", true)]
    [DataRow("ClientId", "false", true)]
    public async Task Oidc_client_fields_follow_existing_uri_policy(string field, string value, bool expected)
    {
        var client = JsonNode.Parse("""{"ClientId":"client-signing-probe","ClientSecret":"private-signing-probe","RedirectUris":["https://client.example.invalid/callback"]}""")!.AsObject();
        client[field] = JsonNode.Parse(value);
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { ["Clients"] = new JsonArray(client) }.ToJsonString()));
        Assert.AreEqual(expected, RuntimeOidcValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", expected ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("client-signing-probe", false)]
    [DataRow("CLIENT-SIGNING-PROBE", true)]
    public async Task Oidc_client_ids_keep_ordinal_uniqueness(string secondId, bool expected)
    {
        var client = JsonNode.Parse("""{"ClientId":"client-signing-probe","RedirectUris":["https://client.example.invalid/callback"]}""")!.AsObject();
        var other = client.DeepClone();
        other["ClientId"] = secondId;
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { ["Clients"] = new JsonArray(client, other) }.ToJsonString()));
        Assert.AreEqual(expected, RuntimeOidcValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", expected ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("base", "development")]
    [DataRow("profile", "development")]
    [DataRow("secrets", "development")]
    [DataRow("environment", "development")]
    [DataRow("base", "production")]
    [DataRow("profile", "production")]
    [DataRow("environment", "production")]
    public async Task Oidc_clients_follow_host_configuration_sources(string source, string profile)
    {
        using var fixture = new Workspace(ReadyOidcIdentity(source == "base" ? "{}" : "{\"Clients\":[]}"));
        const string overlay = """{"identity:oidc:clients:0:clientid":"client-signing-probe","IDENTITY:OIDC:CLIENTS:0:REDIRECTURIS:0":"https://client.example.invalid/callback"}""";
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
            foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, string>>(overlay)!)
                Environment.SetEnvironmentVariable(entry.Key.Replace(":", "__"), entry.Value);
        await AssertDiagnostic(fixture, profile, "DIAG_OIDC_CLIENTS_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("ClientId", "\"\"")]
    [DataRow("ClientId", "null")]
    [DataRow("entry", "null")]
    [DataRow("entry", "{}")]
    [DataRow("collection", "null")]
    [DataRow("collection", "[]")]
    public async Task Oidc_client_empty_parent_and_leaf_overrides_match_binder(string field, string value)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{}"));
        var path = "Identity:Oidc:Clients" + (field == "collection" ? "" : field == "entry" ? ":0" : ":0:" + field);
        var overlay = new JsonObject { [path] = JsonNode.Parse(value) }.ToJsonString();
        var valid = RuntimeOidcValid(fixture.Configuration, "development", overlay);
        fixture.WriteProfile("development", overlay);
        await AssertDiagnostic(fixture, "development", valid ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("secrets")]
    [DataRow("environment")]
    public async Task Oidc_client_invalid_leaf_override_is_not_hidden(string source)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{}"));
        const string overlay = """{"Identity:Oidc:Clients:0:ClientId":""}""";
        Assert.IsFalse(RuntimeOidcValid(fixture.Configuration, "development", overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteSecrets("""{"Identity:Oidc:Clients:0:ClientId":"other-signing-probe"}""");
            Environment.SetEnvironmentVariable("Identity__Oidc__Clients__0__ClientId", "");
        }
        await AssertDiagnostic(fixture, "development", "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("development")]
    [DataRow("production")]
    public async Task Disabled_oidc_does_not_require_clients(string profile)
    {
        using var fixture = new Workspace(ReadyOidcIdentity("""{"Enable":false,"Clients":null}"""));
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, "DIAG_OIDC_DISABLED ok", "DIAG_OIDC_CLIENTS_");
    }

    [TestMethod]
    public async Task Oidc_clients_production_ignores_development_secrets()
    {
        using var fixture = new Workspace(ReadyOidcIdentity("{}"));
        fixture.WriteSecrets("""{"Identity:Oidc:Clients:0:ClientId":""}""");
        Assert.IsTrue(RuntimeOidcValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", "DIAG_OIDC_CLIENTS_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow("RedirectUris")]
    [DataRow("PostLogoutRedirectUris")]
    public async Task Oidc_uri_array_unbindable_object_items_match_binder(string field)
    {
        var client = JsonNode.Parse("""{"ClientId":"client-signing-probe","RedirectUris":["https://client.example.invalid/callback"]}""")!.AsObject();
        client[field] = JsonNode.Parse("""[{"Other":"invalid-signing-probe"}]""");
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { ["Clients"] = new JsonArray(client) }.ToJsonString()));
        var valid = RuntimeOidcValid(fixture.Configuration, "production");
        await AssertDiagnostic(fixture, "production", valid ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("false", false)]
    [DataRow("\"invalid-signing-probe\"", false)]
    [DataRow("\"invalid-signing-probe\"", true)]
    public async Task Oidc_client_unbindable_optional_boolean_follows_array_binding(string value, bool additionalClient)
    {
        var client = JsonNode.Parse("""{"ClientId":"client-signing-probe","RedirectUris":["https://client.example.invalid/callback"]}""")!.AsObject();
        var other = client.DeepClone();
        other["ClientId"] = "other-signing-probe";
        client["IsFirstParty"] = JsonNode.Parse(value);
        var clients = new JsonArray(client);
        if (additionalClient) clients.Add(other);
        using var fixture = new Workspace(ReadyOidcIdentity(new JsonObject { ["Clients"] = clients }.ToJsonString()));
        var valid = RuntimeOidcValid(fixture.Configuration, "production");
        await AssertDiagnostic(fixture, "production", valid ? "DIAG_OIDC_CLIENTS_CONFIGURED ok" : "DIAG_OIDC_CLIENTS_INVALID error");
    }

    [TestMethod]
    [DataRow("AccessTokenMinutes", "1", true)]
    [DataRow("AccessTokenMinutes", "60", true)]
    [DataRow("AccessTokenMinutes", "0", false)]
    [DataRow("AccessTokenMinutes", "61", false)]
    [DataRow("RefreshTokenDays", "1", true)]
    [DataRow("RefreshTokenDays", "90", true)]
    [DataRow("RefreshTokenDays", "0", false)]
    [DataRow("RefreshTokenDays", "91", false)]
    [DataRow("LockoutThreshold", "1", true)]
    [DataRow("LockoutThreshold", "20", true)]
    [DataRow("LockoutThreshold", "0", false)]
    [DataRow("LockoutThreshold", "21", false)]
    [DataRow("LockoutMinutes", "1", true)]
    [DataRow("LockoutMinutes", "1440", true)]
    [DataRow("LockoutMinutes", "0", false)]
    [DataRow("LockoutMinutes", "1441", false)]
    [DataRow("LoginRateLimitPermitLimitPerMinute", "1", true)]
    [DataRow("LoginRateLimitPermitLimitPerMinute", "2147483647", true)]
    [DataRow("LoginRateLimitPermitLimitPerMinute", "0", false)]
    [DataRow("SessionMutationRateLimitPermitLimitPerMinute", "1", true)]
    [DataRow("SessionMutationRateLimitPermitLimitPerMinute", "2147483647", true)]
    [DataRow("SessionMutationRateLimitPermitLimitPerMinute", "0", false)]
    [DataRow("PasswordExpirationDays", "0", true)]
    [DataRow("PasswordExpirationDays", "2147483647", true)]
    [DataRow("PasswordExpirationDays", "-1", false)]
    public async Task Numeric_identity_limits_match_real_options_even_with_token_endpoints_disabled(
        string field, string value, bool valid)
    {
        using var fixture = new Workspace(new JsonObject {
            ["EnableTokenEndpoints"] = false, [field] = JsonNode.Parse(value),
        }.ToJsonString());
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", NumericFinding(valid));
    }

    [TestMethod]
    [DataRow("\"numeric-signing-probe\"", false)]
    [DataRow("\"\"", false)]
    [DataRow("false", false)]
    [DataRow("null", false)]
    [DataRow("{}", false)]
    [DataRow("[]", false)]
    [DataRow("\"2147483648\"", false)]
    [DataRow("\"1.5\"", false)]
    [DataRow("1.0", false)]
    [DataRow("\"0x3c\"", true)]
    [DataRow("\"&h3c\"", true)]
    [DataRow("\"#3c\"", true)]
    [DataRow("\"  +60  \"", true)]
    [DataRow("[1]", true)]
    [DataRow("{\"child\":1}", true)]
    public async Task Numeric_identity_conversion_matches_configuration_binder(string value, bool valid)
    {
        using var fixture = new Workspace("{\"EnableTokenEndpoints\":false,\"AccessTokenMinutes\":" + value + "}");
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", NumericFinding(valid));
    }

    [TestMethod]
    [DataRow("base", "development", true)]
    [DataRow("profile", "development", true)]
    [DataRow("secrets", "development", true)]
    [DataRow("environment", "development", true)]
    [DataRow("base", "production", true)]
    [DataRow("profile", "production", true)]
    [DataRow("environment", "production", true)]
    [DataRow("base", "development", false)]
    [DataRow("profile", "development", false)]
    [DataRow("secrets", "development", false)]
    [DataRow("environment", "development", false)]
    [DataRow("base", "production", false)]
    [DataRow("profile", "production", false)]
    [DataRow("environment", "production", false)]
    public async Task Numeric_identity_limits_use_the_final_host_configuration_leaf(string source, string profile, bool valid)
    {
        var value = valid ? "60" : "0";
        using var fixture = new Workspace("{\"EnableTokenEndpoints\":false,\"AccessTokenMinutes\":" + (source == "base" ? value : "61") + "}");
        var overlay = "{\"identity:accesstokenminutes\":\"" + value + "\"}";
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteProfile(profile, """{"Identity:AccessTokenMinutes":62}""");
            if (profile == "development") fixture.WriteSecrets("""{"Identity:AccessTokenMinutes":63}""");
            Environment.SetEnvironmentVariable("identity__accesstokenminutes", value);
        }
        await AssertDiagnostic(fixture, profile, NumericFinding(valid));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    public async Task Empty_identity_parent_does_not_remove_the_lower_numeric_leaf(string value)
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false,"LockoutThreshold":21}""");
        var overlay = "{\"Identity\":" + value + "}";
        Assert.IsFalse(RuntimeValid(fixture.Configuration, "development", overlay));
        fixture.WriteProfile("development", overlay);
        await AssertDiagnostic(fixture, "development", NumericFinding(false));
    }

    [TestMethod]
    public async Task Missing_numeric_identity_options_keep_runtime_defaults()
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false}""");
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", NumericFinding(true));
    }

    [TestMethod]
    public async Task Production_numeric_identity_options_ignore_development_user_secrets()
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false,"AccessTokenMinutes":60}""");
        fixture.WriteSecrets("""{"Identity:AccessTokenMinutes":61}""");
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", NumericFinding(true));
    }

    [TestMethod]
    [DataRow("Issuer", "null", false)]
    [DataRow("Issuer", "\"\"", false)]
    [DataRow("Issuer", "\" \"", false)]
    [DataRow("Issuer", "{}", false)]
    [DataRow("Issuer", "[]", false)]
    [DataRow("Issuer", "\"identity-signing-probe\"", true)]
    [DataRow("Issuer", "42", true)]
    [DataRow("Issuer", "false", true)]
    [DataRow("Issuer", "[1]", true)]
    [DataRow("Issuer", "{\"child\":1}", true)]
    [DataRow("Audience", "null", false)]
    [DataRow("Audience", "\"\"", false)]
    [DataRow("Audience", "\" \"", false)]
    [DataRow("Audience", "{}", false)]
    [DataRow("Audience", "[]", false)]
    [DataRow("Audience", "\"identity-signing-probe\"", true)]
    [DataRow("Audience", "42", true)]
    [DataRow("Audience", "false", true)]
    [DataRow("Audience", "[1]", true)]
    [DataRow("Audience", "{\"child\":1}", true)]
    [DataRow("ClientId", "null", false)]
    [DataRow("ClientId", "\"\"", false)]
    [DataRow("ClientId", "\" \"", false)]
    [DataRow("ClientId", "{}", false)]
    [DataRow("ClientId", "[]", false)]
    [DataRow("ClientId", "\"identity-signing-probe\"", true)]
    [DataRow("ClientId", "42", true)]
    [DataRow("ClientId", "false", true)]
    [DataRow("ClientId", "[1]", true)]
    [DataRow("ClientId", "{\"child\":1}", true)]
    public async Task Identity_protocol_identifiers_follow_runtime_string_binding(string field, string value, bool valid)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = JsonNode.Parse(value) }.ToJsonString());
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", ProtocolFinding(field, valid));
    }

    [TestMethod]
    [DataRow("\"" + nameof(IdentitySessionLoginPolicy.AllowMultiple) + "\"", true)]
    [DataRow("\"" + nameof(IdentitySessionLoginPolicy.SingleSession) + "\"", true)]
    [DataRow("\"" + nameof(IdentitySessionLoginPolicy.SingleSessionPerClient) + "\"", true)]
    [DataRow("\" singlesessionperclient \"", true)]
    [DataRow("0", true)]
    [DataRow("1", true)]
    [DataRow("2", true)]
    [DataRow("\"2\"", true)]
    [DataRow("3", false)]
    [DataRow("-1", false)]
    [DataRow("2147483648", false)]
    [DataRow("\"2147483648\"", false)]
    [DataRow("\"session-signing-probe\"", false)]
    [DataRow("false", false)]
    [DataRow("\"\"", false)]
    [DataRow("null", true)]
    [DataRow("{}", true)]
    [DataRow("[]", false)]
    [DataRow("[1]", true)]
    [DataRow("{\"child\":1}", true)]
    [DataRow("\"#2\"", false)]
    [DataRow("\"0x2\"", false)]
    [DataRow("\"SingleSession, SingleSessionPerClient\"", false)]
    [DataRow("\"AllowMultiple, SingleSession\"", true)]
    [DataRow("1.0", false)]
    [DataRow("\"1.5\"", false)]
    [DataRow("\" 2 \"", true)]
    [DataRow("\"-1\"", false)]
    [DataRow("true", false)]
    [DataRow("\" \"", false)]
    public async Task Identity_protocol_session_policy_matches_actual_binder_and_validator(string value, bool valid)
    {
        using var fixture = new Workspace("{\"EnableTokenEndpoints\":false,\"SessionLoginPolicy\":" + value + "}");
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", ProtocolFinding("SessionLoginPolicy", valid));
    }

    [TestMethod]
    [DataRow("Issuer", "base", "development")]
    [DataRow("Issuer", "profile", "development")]
    [DataRow("Issuer", "secrets", "development")]
    [DataRow("Issuer", "environment", "development")]
    [DataRow("Issuer", "base", "production")]
    [DataRow("Issuer", "profile", "production")]
    [DataRow("Issuer", "environment", "production")]
    [DataRow("Audience", "base", "development")]
    [DataRow("Audience", "profile", "development")]
    [DataRow("Audience", "secrets", "development")]
    [DataRow("Audience", "environment", "development")]
    [DataRow("Audience", "base", "production")]
    [DataRow("Audience", "profile", "production")]
    [DataRow("Audience", "environment", "production")]
    [DataRow("ClientId", "base", "development")]
    [DataRow("ClientId", "profile", "development")]
    [DataRow("ClientId", "secrets", "development")]
    [DataRow("ClientId", "environment", "development")]
    [DataRow("ClientId", "base", "production")]
    [DataRow("ClientId", "profile", "production")]
    [DataRow("ClientId", "environment", "production")]
    [DataRow("SessionLoginPolicy", "base", "development")]
    [DataRow("SessionLoginPolicy", "profile", "development")]
    [DataRow("SessionLoginPolicy", "secrets", "development")]
    [DataRow("SessionLoginPolicy", "environment", "development")]
    [DataRow("SessionLoginPolicy", "base", "production")]
    [DataRow("SessionLoginPolicy", "profile", "production")]
    [DataRow("SessionLoginPolicy", "environment", "production")]
    public async Task Identity_protocol_invalid_final_leaf_is_not_hidden(string field, string source, string profile)
    {
        var invalid = field == "SessionLoginPolicy" ? "3" : "";
        using var fixture = new Workspace(new JsonObject {
            ["EnableTokenEndpoints"] = false, [field] = source == "base" ? invalid : ProtocolValue(field),
        }.ToJsonString());
        var overlay = new JsonObject { ["identity:" + field.ToLowerInvariant()] = invalid }.ToJsonString();
        Assert.IsFalse(RuntimeValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteProfile(profile, new JsonObject { ["Identity:" + field] = ProtocolValue(field) }.ToJsonString());
            if (profile == "development") fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = ProtocolValue(field) }.ToJsonString());
            Environment.SetEnvironmentVariable("identity__" + field.ToLowerInvariant(), invalid);
        }
        await AssertDiagnostic(fixture, profile, ProtocolFinding(field, false));
    }

    [TestMethod]
    [DataRow("Issuer", "profile")]
    [DataRow("Issuer", "secrets")]
    [DataRow("Issuer", "environment")]
    [DataRow("SessionLoginPolicy", "profile")]
    [DataRow("SessionLoginPolicy", "secrets")]
    [DataRow("SessionLoginPolicy", "environment")]
    public async Task Identity_protocol_valid_overlay_repairs_lower_invalid_leaf(string field, string source)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = "" }.ToJsonString());
        var overlay = new JsonObject { ["Identity:" + field] = ProtocolValue(field) }.ToJsonString();
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "development", overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = "" }.ToJsonString());
            Environment.SetEnvironmentVariable("Identity__" + field, ProtocolValue(field));
        }
        await AssertDiagnostic(fixture, "development", ProtocolFinding(field, true));
    }

    [TestMethod]
    [DataRow("Issuer")]
    [DataRow("SessionLoginPolicy")]
    public async Task Identity_protocol_production_ignores_development_user_secrets(string field)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = ProtocolValue(field) }.ToJsonString());
        fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = "" }.ToJsonString());
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", ProtocolFinding(field, true));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    public async Task Identity_protocol_empty_parent_preserves_lower_identifier(string value)
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false,"Issuer":""}""");
        var overlay = "{\"Identity\":" + value + "}";
        Assert.IsFalse(RuntimeValid(fixture.Configuration, "development", overlay));
        fixture.WriteProfile("development", overlay);
        await AssertDiagnostic(fixture, "development", ProtocolFinding("Issuer", false));
    }

    [TestMethod]
    public async Task Identity_protocol_missing_fields_keep_runtime_defaults()
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false}""");
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", ProtocolFinding("Issuer", true));
        await AssertDiagnostic(fixture, "production", ProtocolFinding("SessionLoginPolicy", true));
    }

    [TestMethod]
    [DataRow("RequireSecureCookies", "true", true)]
    [DataRow("RequireSecureCookies", "false", true)]
    [DataRow("RequireSecureCookies", "\" TrUe \"", true)]
    [DataRow("RequireSecureCookies", "\"false\"", true)]
    [DataRow("RequireSecureCookies", "null", true)]
    [DataRow("RequireSecureCookies", "{}", true)]
    [DataRow("RequireSecureCookies", "[]", false)]
    [DataRow("RequireSecureCookies", "[true]", true)]
    [DataRow("RequireSecureCookies", "{\"child\":true}", true)]
    [DataRow("RequireSecureCookies", "\"\"", false)]
    [DataRow("RequireSecureCookies", "\" \"", false)]
    [DataRow("RequireSecureCookies", "\"boolean-signing-probe\"", false)]
    [DataRow("RequireSecureCookies", "1", false)]
    [DataRow("RequireSecureCookies", "1.0", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "true", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "false", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "\" TrUe \"", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "\"false\"", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "null", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "{}", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "[]", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "[true]", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "{\"child\":true}", true)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "\"\"", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "\" \"", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "\"boolean-signing-probe\"", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "1", false)]
    [DataRow("EnableRemoteSuperAdministratorManagement", "1.0", false)]
    [DataRow("EnableTotpStrongReauthentication", "true", true)]
    [DataRow("EnableTotpStrongReauthentication", "false", true)]
    [DataRow("EnableTotpStrongReauthentication", "\" TrUe \"", true)]
    [DataRow("EnableTotpStrongReauthentication", "\"false\"", true)]
    [DataRow("EnableTotpStrongReauthentication", "null", true)]
    [DataRow("EnableTotpStrongReauthentication", "{}", true)]
    [DataRow("EnableTotpStrongReauthentication", "[]", false)]
    [DataRow("EnableTotpStrongReauthentication", "[true]", true)]
    [DataRow("EnableTotpStrongReauthentication", "{\"child\":true}", true)]
    [DataRow("EnableTotpStrongReauthentication", "\"\"", false)]
    [DataRow("EnableTotpStrongReauthentication", "\" \"", false)]
    [DataRow("EnableTotpStrongReauthentication", "\"boolean-signing-probe\"", false)]
    [DataRow("EnableTotpStrongReauthentication", "1", false)]
    [DataRow("EnableTotpStrongReauthentication", "1.0", false)]
    public async Task Identity_security_flags_match_real_boolean_binding(string field, string value, bool valid)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = JsonNode.Parse(value) }.ToJsonString());
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "development"));
        await AssertDiagnostic(fixture, "development", SecurityFinding(valid));
    }

    [TestMethod]
    [DataRow("production", "false", "false", true)]
    [DataRow("production", "false", "true", true)]
    [DataRow("production", "true", "false", false)]
    [DataRow("production", "true", "true", true)]
    [DataRow("development", "false", "false", true)]
    [DataRow("development", "false", "true", true)]
    [DataRow("development", "true", "false", true)]
    [DataRow("development", "true", "true", true)]
    [DataRow("production", "null", "false", true)]
    [DataRow("production", "{}", "false", true)]
    [DataRow("production", "true", "null", false)]
    [DataRow("production", "true", "{}", false)]
    public async Task Identity_security_remote_admin_follows_runtime_profile_guard(string profile, string remote, string totp, bool valid)
    {
        using var fixture = new Workspace(new JsonObject {
            ["EnableTokenEndpoints"] = false, ["EnableRemoteSuperAdministratorManagement"] = JsonNode.Parse(remote),
            ["EnableTotpStrongReauthentication"] = JsonNode.Parse(totp),
        }.ToJsonString());
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, profile));
        await AssertDiagnostic(fixture, profile, SecurityGuardFinding(valid));
    }

    [TestMethod]
    [DataRow("RequireSecureCookies", "base", "development")]
    [DataRow("RequireSecureCookies", "profile", "development")]
    [DataRow("RequireSecureCookies", "secrets", "development")]
    [DataRow("RequireSecureCookies", "environment", "development")]
    [DataRow("RequireSecureCookies", "base", "production")]
    [DataRow("RequireSecureCookies", "profile", "production")]
    [DataRow("RequireSecureCookies", "environment", "production")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "base", "development")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "profile", "development")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "secrets", "development")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "environment", "development")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "base", "production")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "profile", "production")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "environment", "production")]
    [DataRow("EnableTotpStrongReauthentication", "base", "development")]
    [DataRow("EnableTotpStrongReauthentication", "profile", "development")]
    [DataRow("EnableTotpStrongReauthentication", "secrets", "development")]
    [DataRow("EnableTotpStrongReauthentication", "environment", "development")]
    [DataRow("EnableTotpStrongReauthentication", "base", "production")]
    [DataRow("EnableTotpStrongReauthentication", "profile", "production")]
    [DataRow("EnableTotpStrongReauthentication", "environment", "production")]
    public async Task Identity_security_invalid_boolean_final_leaf_is_not_hidden(string field, string source, string profile)
    {
        const string invalid = "boolean-signing-probe";
        using var fixture = new Workspace(new JsonObject {
            ["EnableTokenEndpoints"] = false, [field] = source == "base" ? JsonValue.Create(invalid) : JsonValue.Create(false),
        }.ToJsonString());
        var overlay = new JsonObject { ["identity:" + field.ToLowerInvariant()] = invalid }.ToJsonString();
        Assert.IsFalse(RuntimeValid(fixture.Configuration, profile, source == "base" ? null : overlay));
        if (source == "profile") fixture.WriteProfile(profile, overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteProfile(profile, new JsonObject { ["Identity:" + field] = false }.ToJsonString());
            if (profile == "development") fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = false }.ToJsonString());
            Environment.SetEnvironmentVariable("identity__" + field.ToLowerInvariant(), invalid);
        }
        await AssertDiagnostic(fixture, profile, SecurityFinding(false));
    }

    [TestMethod]
    [DataRow("RequireSecureCookies", "profile")]
    [DataRow("RequireSecureCookies", "secrets")]
    [DataRow("RequireSecureCookies", "environment")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "profile")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "secrets")]
    [DataRow("EnableRemoteSuperAdministratorManagement", "environment")]
    [DataRow("EnableTotpStrongReauthentication", "profile")]
    [DataRow("EnableTotpStrongReauthentication", "secrets")]
    [DataRow("EnableTotpStrongReauthentication", "environment")]
    public async Task Identity_security_valid_overlay_repairs_invalid_boolean(string field, string source)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = "boolean-signing-probe" }.ToJsonString());
        var overlay = new JsonObject { ["Identity:" + field] = true }.ToJsonString();
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "development", overlay));
        if (source == "profile") fixture.WriteProfile("development", overlay);
        if (source == "secrets") fixture.WriteSecrets(overlay);
        if (source == "environment")
        {
            fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = "boolean-signing-probe" }.ToJsonString());
            Environment.SetEnvironmentVariable("Identity__" + field, "true");
        }
        await AssertDiagnostic(fixture, "development", SecurityFinding(true));
    }

    [TestMethod]
    [DataRow("RequireSecureCookies")]
    [DataRow("EnableRemoteSuperAdministratorManagement")]
    [DataRow("EnableTotpStrongReauthentication")]
    public async Task Identity_security_production_ignores_development_secrets(string field)
    {
        using var fixture = new Workspace(new JsonObject { ["EnableTokenEndpoints"] = false, [field] = false }.ToJsonString());
        fixture.WriteSecrets(new JsonObject { ["Identity:" + field] = "boolean-signing-probe" }.ToJsonString());
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", SecurityFinding(true));
    }

    [TestMethod]
    [DataRow("EnableRemoteSuperAdministratorManagement", true, false, "profile")]
    [DataRow("EnableRemoteSuperAdministratorManagement", false, true, "profile")]
    [DataRow("EnableTotpStrongReauthentication", false, false, "profile")]
    [DataRow("EnableTotpStrongReauthentication", true, true, "profile")]
    [DataRow("EnableRemoteSuperAdministratorManagement", true, false, "environment")]
    [DataRow("EnableRemoteSuperAdministratorManagement", false, true, "environment")]
    [DataRow("EnableTotpStrongReauthentication", false, false, "environment")]
    [DataRow("EnableTotpStrongReauthentication", true, true, "environment")]
    public async Task Identity_security_remote_admin_guard_uses_final_leaves(string field, bool value, bool valid, string source)
    {
        var identity = new JsonObject {
            ["EnableTokenEndpoints"] = false, ["EnableRemoteSuperAdministratorManagement"] = true,
            ["EnableTotpStrongReauthentication"] = false, [field] = !value,
        };
        using var fixture = new Workspace(identity.ToJsonString());
        var overlay = new JsonObject { ["Identity:" + field] = value }.ToJsonString();
        Assert.AreEqual(valid, RuntimeValid(fixture.Configuration, "production", overlay));
        if (source == "profile") fixture.WriteProfile("production", overlay);
        else
        {
            fixture.WriteProfile("production", new JsonObject { ["Identity:" + field] = !value }.ToJsonString());
            Environment.SetEnvironmentVariable("Identity__" + field, value.ToString());
        }
        await AssertDiagnostic(fixture, "production", SecurityGuardFinding(valid));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    public async Task Identity_security_empty_parent_keeps_remote_admin_leaves(string parent)
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false,"EnableRemoteSuperAdministratorManagement":true}""");
        var overlay = "{\"Identity\":" + parent + "}";
        Assert.IsFalse(RuntimeValid(fixture.Configuration, "production", overlay));
        fixture.WriteProfile("production", overlay);
        await AssertDiagnostic(fixture, "production", SecurityGuardFinding(false));
    }

    [TestMethod]
    public async Task Identity_security_missing_flags_keep_runtime_defaults()
    {
        using var fixture = new Workspace("""{"EnableTokenEndpoints":false}""");
        Assert.IsTrue(RuntimeValid(fixture.Configuration, "production"));
        await AssertDiagnostic(fixture, "production", SecurityFinding(true));
    }

    private static string SecurityFinding(bool valid) => valid
        ? "DIAG_IDENTITY_SECURITY_OPTIONS_CONFIGURED ok" : "DIAG_IDENTITY_SECURITY_OPTIONS_INVALID error";
    private static string SecurityGuardFinding(bool valid) => valid
        ? SecurityFinding(true) : "DIAG_IDENTITY_REMOTE_ADMIN_REAUTH_REQUIRED error";

    private static string ProtocolValue(string field) => field == "SessionLoginPolicy" ? "SingleSessionPerClient" : "identity-signing-probe";
    private static string ProtocolFinding(string field, bool valid) =>
        "DIAG_IDENTITY_" + (field == "SessionLoginPolicy" ? "SESSION_POLICY" : "IDENTIFIERS")
        + (valid ? "_CONFIGURED ok" : "_INVALID error");

    private static string NumericFinding(bool valid) => valid
        ? "DIAG_IDENTITY_NUMERIC_OPTIONS_CONFIGURED ok" : "DIAG_IDENTITY_NUMERIC_OPTIONS_INVALID error";

    private static string SyntheticEncryptionKey(int bytes) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes("encryption-signing-probe".PadRight(bytes, 'x')));

    private static string ReadyOidcIdentity(string fragment)
    {
        var identity = JsonNode.Parse(OidcIdentity("""{"ActiveSigningKeyId":"oidc-signing-probe","SigningKeys":{"oidc-signing-probe":{"PublicKeyPem":"public-signing-probe","PrivateKeyPem":"private-signing-probe"}}}"""))!.AsObject();
        foreach (var entry in JsonNode.Parse(fragment)!.AsObject()) identity["Oidc"]![entry.Key] = entry.Value?.DeepClone();
        return identity.ToJsonString();
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

    private static async Task AssertDiagnostic(Workspace fixture, string profile, string finding, string? absentFinding = null, string? secret = null)
    {
        var before = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        var text = output.ToString() + error;
        StringAssert.Contains(text, "DIAG_SDK_OK ok");
        StringAssert.Contains(text, finding);
        if (absentFinding is not null) Assert.IsFalse(text.Contains(absentFinding, StringComparison.Ordinal));
        if (secret is not null) Assert.IsFalse(text.Contains(secret, StringComparison.Ordinal));
        Assert.AreEqual(finding.EndsWith(" error", StringComparison.Ordinal) ? 1 : 0, result);
        Assert.IsFalse(text.Contains("signing-probe", StringComparison.Ordinal));
        foreach (var bytes in new[] { 31, 32, 33 })
            Assert.IsFalse(text.Contains(SyntheticEncryptionKey(bytes), StringComparison.Ordinal));
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
