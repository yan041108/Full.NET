using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class DiagnoseCommandTests
{
    [TestMethod]
    public async Task Unknown_profile_is_rejected_without_development_fallback()
    {
        using var fixture = new DiagnoseWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "prodution"], output, error);

        Assert.AreEqual(64, result);
        StringAssert.Contains(error.ToString(), "--profile");
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    [DataRow("development")]
    [DataRow("production")]
    public async Task Supported_profile_reaches_diagnostics(string profile)
    {
        using var fixture = new DiagnoseWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);

        Assert.AreNotEqual(64, result);
        StringAssert.Contains(output.ToString(), "DIAG_SDK_");
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{\"FullNet\":\"credential-probe\"}")]
    [DataRow("{\"FullNet\":{\"Modules\":{\"Preset\":42}}}")]
    [DataRow("{\"FullNet\":{\"Modules\":{\"Enabled\":\"credential-probe\"}}}")]
    [DataRow("{\"Database\":{\"ConnectionName\":[]}}")]
    [DataRow("{\"ConnectionStrings\":\"credential-probe\"}")]
    [DataRow("{\"Cache\":{\"RedisConnectionString\":true}}")]
    [DataRow("{\"Realtime\":{\"RedisBackplaneConnectionString\":42}}")]
    [DataRow("{\"credential-probe\":1,\"credential-probe\":2}")]
    [DataRow("{\"Cache\":{\"credential-probe\":1,\"credential-probe\":2}}")]
    public async Task Invalid_configuration_shape_returns_redacted_machine_diagnostic(string configuration)
    {
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var before = File.ReadAllBytes(fixture.Settings);

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_APPSETTINGS_INVALID error");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
    }

    [TestMethod]
    public async Task Sdk_probe_respects_target_workspace_global_json()
    {
        using var fixture = new DiagnoseWorkspace();
        File.WriteAllText(Path.Combine(fixture.Root, "global.json"),
            """{"sdk":{"version":"99.0.100","rollForward":"disable"}}""", new UTF8Encoding(false));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_SDK_MISSING error");
        Assert.IsFalse(output.ToString().Contains("DIAG_SDK_OK", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cancelled_diagnosis_propagates_cancellation()
    {
        using var fixture = new DiagnoseWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root], output, error, cancellation.Token));
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    [DataRow("<your-connection>")]
    [DataRow("CHANGEME")]
    [DataRow("YOUR_CONNECTION_STRING")]
    [DataRow(" ")]
    public async Task Production_environment_placeholder_does_not_count_as_configured_connection(string connection)
    {
        var connectionName = $"diagnose_{Guid.NewGuid():N}";
        var environmentName = $"ConnectionStrings__{connectionName}";
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(
            new { Database = new { ConnectionName = connectionName } }));
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable(environmentName, connection);
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
            Assert.IsFalse(output.ToString().Contains("DIAG_CONNECTION_CONFIGURED", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(connection))
            {
                Assert.IsFalse((output.ToString() + error).Contains(connection, StringComparison.Ordinal));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
        }
    }

    [TestMethod]
    public async Task Production_configured_connection_is_recognized_without_writing_or_revealing_value()
    {
        var connectionName = $"diagnose_{Guid.NewGuid():N}";
        var environmentName = $"ConnectionStrings__{connectionName}";
        const string connection = "Server=example.invalid;Database=probe;Password=credential-probe";
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(
            new { Database = new { ConnectionName = connectionName } }));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var beforeFiles = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        try
        {
            Environment.SetEnvironmentVariable(environmentName, connection);
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(0, result);
            StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(beforeFiles.Keys.ToArray(),
                Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
            foreach (var (path, bytes) in beforeFiles)
            {
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
        }
    }

    [TestMethod]
    [DataRow("{\"ConnectionStrings:other\":\"Password=credential-probe\"}", false)]
    [DataRow("{\"ConnectionStrings:fullnet\":\"<your-connection>\"}", false)]
    [DataRow("{\"ConnectionStrings\":{\"fullnet\":42}}", false)]
    [DataRow("{\"ConnectionStrings:fullnet\":\"Password=credential-probe\",\"connectionstrings:FULLNET\":\"Password=other\"}", true)]
    [DataRow("{\"ConnectionStrings:fullnet\":\"Password=credential-probe\",\"ConnectionStrings\":{\"fullnet\":\"<your-connection>\"}}", true)]
    [DataRow("{invalid-json", true)]
    public async Task Development_user_secrets_without_usable_target_connection_stay_unconfigured(
        string secrets, bool invalidFile)
    {
        using var fixture = new DiagnoseWorkspace("{\"Database\":{\"ConnectionName\":\"fullnet\"}}");
        fixture.AddStandaloneUserSecrets(secrets, "App.Host.Api");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

        Assert.AreEqual(invalidFile ? 1 : 0, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_PLACEHOLDER warn");
        if (invalidFile)
        {
            StringAssert.Contains(output.ToString(), "DIAG_USER_SECRETS_INVALID error");
        }
        Assert.IsFalse(output.ToString().Contains("DIAG_CONNECTION_CONFIGURED", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"ConnectionStrings:fullnet\":\"Server=example.invalid;Password=credential-probe\"}")]
    [DataRow("{\"ConnectionStrings\":{\"fullnet\":\"Server=example.invalid;Password=credential-probe\"}}")]
    [DataRow("{\"connectionstrings:FULLNET\":\"Server=example.invalid;Password=credential-probe\"}")]
    [DataRow("{\"connectionStrings\":{\"FullNet\":\"Server=example.invalid;Password=credential-probe\"}}")]
    public async Task Development_user_secrets_target_connection_is_read_from_standalone_api_project(string secrets)
    {
        using var fixture = new DiagnoseWorkspace("{\"Database\":{\"ConnectionName\":\"fullnet\"}}");
        fixture.AddStandaloneUserSecrets(secrets);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

        Assert.AreEqual(0, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Production_does_not_count_development_user_secrets_as_runtime_connection()
    {
        using var fixture = new DiagnoseWorkspace("{\"Database\":{\"ConnectionName\":\"fullnet\"}}");
        fixture.AddStandaloneUserSecrets("{\"ConnectionStrings:fullnet\":\"Server=example.invalid;Password=credential-probe\"}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse(output.ToString().Contains("DIAG_CONNECTION_CONFIGURED", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(" ", 1, "DIAG_CONNECTION_MISSING error")]
    [DataRow("<your-connection>", 1, "DIAG_CONNECTION_MISSING error")]
    [DataRow("Server=example.invalid;Password=credential-probe", 0, "DIAG_CONNECTION_CONFIGURED ok")]
    public async Task Production_inline_connection_uses_same_blank_and_placeholder_boundary(
        string connection, int expectedExitCode, string expectedDiagnostic)
    {
        var connectionName = $"diagnose_{Guid.NewGuid():N}";
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(new
        {
            Database = new { ConnectionName = connectionName },
            ConnectionStrings = new Dictionary<string, string> { [connectionName] = connection },
        }));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        Assert.AreEqual(expectedExitCode, result);
        StringAssert.Contains(output.ToString(), expectedDiagnostic);
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Production_secret_environment_overrides_are_checked_as_effective_values()
    {
        var overrides = new Dictionary<string, string>
        {
            ["Cache__RedisConnectionString"] = "cache.example.invalid:6379,password=credential-probe",
            ["Realtime__RedisBackplaneConnectionString"] = "realtime.example.invalid:6379,password=credential-probe",
            ["FullNet__Cryptography__Sm2PrivateKeys__host-integration-signing"] = "credential-probe",
        };
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":""},
             "Realtime":{"RedisBackplaneConnectionString":""},
             "FullNet":{"Cryptography":{"Sm2PrivateKeys":{"host-integration-signing":""}}}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var before = File.ReadAllBytes(fixture.Settings);
        var original = overrides.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in overrides)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(0, result);
            StringAssert.Contains(output.ToString(), "DIAG_SECRETS_OK ok");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
        finally
        {
            foreach (var (key, value) in original)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [TestMethod]
    [DataRow("development", 0, "DIAG_SECRETS_OK ok")]
    [DataRow("production", 1, "DIAG_SECRETS_PLACEHOLDER error")]
    public async Task Secret_user_secrets_are_used_only_by_development(
        string profile, int expectedExitCode, string expectedFinding)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":""},
             "Realtime":{"RedisBackplaneConnectionString":""},
             "FullNet":{"Cryptography":{"Sm2PrivateKeys":{"host-integration-signing":""}}}}
            """);
        fixture.AddStandaloneUserSecrets("""
            {"Cache:RedisConnectionString":"cache.example.invalid:6379,password=credential-probe",
             "Realtime":{"RedisBackplaneConnectionString":"realtime.example.invalid:6379,password=credential-probe"},
             "FullNet":{"Cryptography":{"Sm2PrivateKeys":{"host-integration-signing":"credential-probe"}}}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);

        Assert.AreEqual(expectedExitCode, result);
        StringAssert.Contains(output.ToString(), expectedFinding);
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Production_environment_placeholder_overrides_nonplaceholder_json_secret()
    {
        const string key = "Cache__RedisConnectionString";
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":"cache.example.invalid:6379,password=credential-probe"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "<your-cache-password>");
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_SECRETS_PLACEHOLDER error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    public async Task Production_environment_placeholder_overrides_nonplaceholder_json_connection()
    {
        const string key = "ConnectionStrings__fullnet";
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "<your-connection>");
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    [DataRow("{\"ConnectionStrings:fullnet\":null}")]
    [DataRow("{invalid-json")]
    public async Task Development_unusable_user_secrets_do_not_restore_json_connection(string secrets)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"}}
            """);
        fixture.AddStandaloneUserSecrets(secrets);
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_PLACEHOLDER warn");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Development_null_user_secret_does_not_restore_json_secret()
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":"cache.example.invalid:6379,password=credential-probe"}}
            """);
        fixture.AddStandaloneUserSecrets("""{"Cache:RedisConnectionString":null}""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_SECRETS_PLACEHOLDER warn");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("connectionstrings__fullnet", "DIAG_CONNECTION_MISSING error")]
    [DataRow("cache__redisconnectionstring", "DIAG_SECRETS_PLACEHOLDER error")]
    public async Task Production_lowercase_environment_keys_override_json_on_linux(
        string key, string expectedFinding)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":"cache.example.invalid:6379,password=credential-probe"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "<your-value>");
            await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            StringAssert.Contains(output.ToString(), expectedFinding);
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    [DataRow("{invalid-json")]
    [DataRow("{\"Cache:RedisConnectionString\":\"cache.example.invalid\",\"cache:redisconnectionstring\":\"other.example.invalid\"}")]
    public async Task Development_invalid_user_secrets_file_is_error_even_with_environment_overrides(
        string secrets)
    {
        using var fixture = new DiagnoseWorkspace("{} ");
        fixture.AddStandaloneUserSecrets(secrets);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var overrides = new Dictionary<string, string>
        {
            ["ConnectionStrings__fullnet"] = "Server=example.invalid;Password=credential-probe",
            ["Cache__RedisConnectionString"] = "cache.example.invalid:6379,password=credential-probe",
            ["Realtime__RedisBackplaneConnectionString"] = "realtime.example.invalid:6379,password=credential-probe",
            ["FullNet__Cryptography__Sm2PrivateKeys__host-integration-signing"] = "credential-probe",
        };
        var original = overrides.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in overrides)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_USER_SECRETS_INVALID error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            Assert.IsFalse((output.ToString() + error).Contains("other.example.invalid", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var (key, value) in original)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [TestMethod]
    [DataRow("{\"Extra\":\"value\",\"Extra\":{}}")]
    [DataRow("{\"Extra\":\"value\",\"Extra\":[]}")]
    public async Task Development_user_secrets_empty_collection_can_replace_prior_scalar(string secrets)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"}}
            """);
        fixture.AddStandaloneUserSecrets(secrets);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

        Assert.AreEqual(0, result, output.ToString());
        Assert.IsFalse(output.ToString().Contains("DIAG_USER_SECRETS_INVALID", StringComparison.Ordinal));
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("development", "Development", "DIAG_CONNECTION_CONFIGURED ok", "DIAG_SECRETS_OK ok")]
    [DataRow("production", "Production", "DIAG_CONNECTION_CONFIGURED ok", "DIAG_SECRETS_OK ok")]
    public async Task Selected_profile_json_replaces_base_placeholders(
        string profile, string fileProfile, string connectionFinding, string secretFinding)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"<your-connection>"},"Cache":{"RedisConnectionString":"CHANGEME"}}
            """);
        File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{fileProfile}.json"), """
            { // JSON 配置提供程序允许注释与尾逗号，并按不区分大小写的扁平路径读取。
              "connectionstrings:FULLNET":"Server=example.invalid;Password=credential-probe",
              "cache:redisconnectionstring":"cache.example.invalid:6379,password=credential-probe",
            }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);

        Assert.AreEqual(0, result, output.ToString());
        StringAssert.Contains(output.ToString(), connectionFinding);
        StringAssert.Contains(output.ToString(), secretFinding);
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("\"<your-value>\"")]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    public async Task Production_profile_empty_values_do_not_restore_base_credentials(string value)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
             "Cache":{"RedisConnectionString":"cache.example.invalid:6379,password=credential-probe"}}
            """);
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"),
            $"{{\"ConnectionStrings:fullnet\":{value},\"Cache:RedisConnectionString\":{value}}}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
        StringAssert.Contains(output.ToString(), "DIAG_SECRETS_PLACEHOLDER error");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{invalid-json-credential-probe")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{\"credential-probe\":\"one\",\"CREDENTIAL-PROBE\":\"two\"}")]
    public async Task Invalid_profile_json_is_error_even_with_valid_environment(string configuration)
    {
        using var fixture = new DiagnoseWorkspace();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var original = Environment.GetEnvironmentVariable("ConnectionStrings__fullnet");
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__fullnet", "Server=example.invalid;Password=credential-probe");
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_APPSETTINGS_INVALID error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__fullnet", original);
        }
    }

    [TestMethod]
    [DataRow("development", false)]
    [DataRow("production", true)]
    public async Task Only_selected_profile_is_loaded_and_development_secrets_override_it(string profile, bool environmentOverride)
    {
        using var fixture = new DiagnoseWorkspace();
        fixture.AddStandaloneUserSecrets("""
            {"ConnectionStrings:fullnet":"Server=example.invalid;Password=credential-probe",
             "Cache:RedisConnectionString":"cache.example.invalid:6379,password=credential-probe"}
            """);
        var selected = profile == "development" ? "Development" : "Production";
        var other = profile == "development" ? "Production" : "Development";
        File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{selected}.json"), """
            {"ConnectionStrings:fullnet":"CHANGEME","Cache:RedisConnectionString":"CHANGEME"}
            """);
        File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{other}.json"), "{invalid-json");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var keys = new[] { "ConnectionStrings__fullnet", "Cache__RedisConnectionString" };
        var original = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            if (environmentOverride)
            {
                foreach (var key in keys)
                {
                    Environment.SetEnvironmentVariable(key, "credential-probe");
                }
            }
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);

            Assert.AreEqual(0, result, output.ToString());
            StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
            StringAssert.Contains(output.ToString(), "DIAG_SECRETS_OK ok");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var (key, value) in original)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [TestMethod]
    public async Task Profile_can_select_a_different_connection_name()
    {
        using var fixture = new DiagnoseWorkspace("""
            {"Database":{"ConnectionName":"fullnet"},"ConnectionStrings":{"fullnet":"credential-probe"}}
            """);
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), """
            {"Database":{"ConnectionName":"production"},"ConnectionStrings":{"production":"CHANGEME"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse(output.ToString().Contains("DIAG_CONNECTION_CONFIGURED", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"Cache\":{}}")]
    [DataRow("{\"Cache\":null}")]
    [DataRow("{\"Cache\":{\"Other\":\"unused\"}}")]
    public async Task Profile_parent_value_does_not_erase_lower_priority_child_key(string configuration)
    {
        using var fixture = new DiagnoseWorkspace("""
            {"ConnectionStrings":{"fullnet":"credential-probe"},"Cache":{"RedisConnectionString":"credential-probe"}}
            """);
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_SECRETS_OK ok");
    }

    [TestMethod]
    public async Task Profile_is_read_beside_selected_host_configuration()
    {
        using var fixture = new DiagnoseWorkspace();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), "{invalid-json");
        var hostPath = Path.Combine(fixture.Root, "src", "Hosts", "Full.NET.Host.Api");
        Directory.CreateDirectory(hostPath);
        File.WriteAllText(Path.Combine(hostPath, "appsettings.json"), """
            {"ConnectionStrings":{"fullnet":"credential-probe"}}
            """);
        File.WriteAllText(Path.Combine(hostPath, "appsettings.Production.json"), """
            {"ConnectionStrings":{"fullnet":"CHANGEME"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse(output.ToString().Contains("DIAG_APPSETTINGS_INVALID", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("development")]
    [DataRow("production")]
    public async Task Environment_placeholders_override_valid_profile_json(string profile)
    {
        using var fixture = new DiagnoseWorkspace();
        var fileProfile = profile == "development" ? "Development" : "Production";
        File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{fileProfile}.json"), """
            {"ConnectionStrings:fullnet":"credential-probe","Cache:RedisConnectionString":"credential-probe"}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var keys = new[] { "ConnectionStrings__fullnet", "Cache__RedisConnectionString" };
        var original = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var key in keys)
            {
                Environment.SetEnvironmentVariable(key, "CHANGEME");
            }
            await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);

            StringAssert.Contains(output.ToString(), profile == "development"
                ? "DIAG_CONNECTION_PLACEHOLDER warn" : "DIAG_CONNECTION_MISSING error");
            StringAssert.Contains(output.ToString(), "DIAG_SECRETS_PLACEHOLDER");
        }
        finally
        {
            foreach (var (key, value) in original)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [TestMethod]
    public async Task Production_user_secrets_cannot_mask_profile_placeholders()
    {
        using var fixture = new DiagnoseWorkspace();
        fixture.AddStandaloneUserSecrets("""{"ConnectionStrings:fullnet":"credential-probe"}""");
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), """
            {"ConnectionStrings:fullnet":"CHANGEME"}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        Assert.AreEqual(1, result);
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
    }

    [TestMethod]
    public async Task Profile_selected_connection_name_resolves_base_key_without_case_sensitivity()
    {
        using var fixture = new DiagnoseWorkspace("""{"ConnectionStrings":{"fullnet":"credential-probe"}}""");
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"), """
            {"Database":{"ConnectionName":"FULLNET"}}
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Empty_json_property_name_preserves_configuration_path_separator(bool hasNormalKey)
    {
        using var fixture = new DiagnoseWorkspace("""{"ConnectionStrings":{"fullnet":"CHANGEME"}}""");
        var normal = hasNormalKey ? ",\"ConnectionStrings:fullnet\":\"CHANGEME\"" : string.Empty;
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Production.json"),
            "{\"\":{\"ConnectionStrings\":{\"fullnet\":\"credential-probe\"}}" + normal + "}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse(output.ToString().Contains("DIAG_APPSETTINGS_INVALID", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Connection_name_override_cannot_echo_misplaced_credentials(bool useUserSecrets)
    {
        using var fixture = new DiagnoseWorkspace();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var original = Environment.GetEnvironmentVariable("Database__ConnectionName");
        try
        {
            if (useUserSecrets)
            {
                fixture.AddStandaloneUserSecrets("""
                    {"Database:ConnectionName":"Server=example.invalid;Password=credential-probe"}
                    """);
            }
            else
            {
                Environment.SetEnvironmentVariable("Database__ConnectionName", "Server=example.invalid;Password=credential-probe");
            }
            await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);

            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__ConnectionName", original);
        }
    }

    [TestMethod]
    [DataRow("10.0.100")]
    [DataRow("10.0.401")]
    [DataRow("10.0.999")]
    [DataRow("10.0.100-preview.7.25380.108")]
    [DataRow("10.0.100-rc.2.25502.107")]
    [DataRow("10.0.401+build.123")]
    [DataRow("10.0.100-credential-probe")]
    public void Supported_sdk_feature_bands_and_prereleases_are_recognized(string version)
    {
        var finding = DiagnoseCommand.DiagnoseSdkVersion(version);

        Assert.AreEqual("DIAG_SDK_OK", finding.Code);
        Assert.AreEqual("ok", finding.Severity);
        Assert.IsFalse(finding.Message.Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("8.0.408")]
    [DataRow("9.0.307")]
    [DataRow("11.0.100")]
    [DataRow("10.1.100")]
    [DataRow("10.0.99")]
    public void Sdk_outside_current_framework_baseline_is_not_reported_as_compatible(string version)
    {
        var finding = DiagnoseCommand.DiagnoseSdkVersion(version);

        Assert.AreEqual("DIAG_SDK_INCOMPATIBLE", finding.Code);
        Assert.AreEqual("error", finding.Severity);
        StringAssert.Contains(finding.Hint!, "global.json");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("credential-probe")]
    [DataRow("10")]
    [DataRow("10.0")]
    [DataRow("10.0.401.1")]
    [DataRow("10.0.401-credential-probe;Password=credential-probe")]
    [DataRow("10.0.401\ncredential-probe")]
    [DataRow("999999999999999999999.0.100")]
    [DataRow("10.0.401-")]
    [DataRow("10.0.401-preview..1")]
    public void Invalid_sdk_probe_output_fails_without_echoing_process_output(string version)
    {
        var finding = DiagnoseCommand.DiagnoseSdkVersion(version);

        Assert.AreEqual("DIAG_SDK_INCOMPATIBLE", finding.Code);
        Assert.AreEqual("error", finding.Severity);
        Assert.IsFalse((finding.Message + finding.Hint).Contains("credential-probe", StringComparison.Ordinal));
    }

    private sealed class DiagnoseWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fullnet-diagnose-{Guid.NewGuid():N}");
        public string Settings => Path.Combine(Root, "appsettings.json");
        private string? userSecretsDirectory;

        public DiagnoseWorkspace(string configuration = "{}")
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Settings, configuration, new UTF8Encoding(false));
            // 正常配置场景固定当前应用基线，避免机器额外安装未来 SDK 后改变测试前提。
            File.WriteAllText(Path.Combine(Root, "global.json"),
                """{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":true}}""",
                new UTF8Encoding(false));
        }

        public void AddStandaloneUserSecrets(string secrets, string hostName = "Demo.Host.Api")
        {
            var id = $"fullnet-diagnose-{Guid.NewGuid():N}";
            var hostDirectory = Path.Combine(Root, "src", hostName);
            Directory.CreateDirectory(hostDirectory);
            File.WriteAllText(Path.Combine(hostDirectory, hostName + ".csproj"),
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UserSecretsId>{id}</UserSecretsId></PropertyGroup></Project>",
                new UTF8Encoding(false));
            userSecretsDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "UserSecrets", id)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".microsoft", "usersecrets", id);
            Directory.CreateDirectory(userSecretsDirectory);
            File.WriteAllText(Path.Combine(userSecretsDirectory, "secrets.json"), secrets, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            if (userSecretsDirectory is not null)
            {
                Directory.Delete(userSecretsDirectory, recursive: true);
            }
        }
    }
}
