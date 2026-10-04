using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    [DataRow("// credential-probe comment\n\"Probe\":1", true)]
    [DataRow("\"Probe\":[1,2,],", true)]
    [DataRow("/* credential-probe comment */\"Probe\":{\"Value\":1,},", true)]
    [DataRow("\"credential-probe\":1,\"CREDENTIAL-PROBE\":2", false)]
    [DataRow("\"Probe\":{\"credential-probe\":1,\"CREDENTIAL-PROBE\":2}", false)]
    [DataRow("\"Probe:credential-probe\":1,\"probe\":{\"CREDENTIAL-PROBE\":2}", false)]
    [DataRow("\"Probe\":[1],\"probe:0\":\"credential-probe\"", false)]
    [DataRow("\"Probe\":{\"Value\":1},\"probe\":{\"value\":\"credential-probe\"}", false)]
    [DataRow("\"credential-probe\":{},\"CREDENTIAL-PROBE\":1", false)]
    [DataRow("\"credential-probe\":[],\"CREDENTIAL-PROBE\":1", false)]
    [DataRow("\"credential-probe\":1,\"CREDENTIAL-PROBE\":{}", true)]
    [DataRow("\"credential-probe\":1,\"CREDENTIAL-PROBE\":[]", true)]
    [DataRow("\"Probe\":{\"One\":1},\"probe\":{\"Two\":2}", true)]
    [DataRow("\"Probe\":{\"\":1},\"probe:\":\"credential-probe\"", false)]
    public async Task Base_json_matches_configuration_loader_syntax_and_path_validation(string fragment, bool valid)
    {
        var configuration = """
            {"Database":{"Provider":"MySql","MySqlGuidStorageMode":"Binary16"},
            "ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
            """ + fragment + "}";
        // 先由实际 JSON 配置提供程序判定语法和路径冲突；高优先级有效值不能修复坏文件。
        if (valid)
        {
            Assert.IsTrue(RuntimeDatabaseOptionsAreValid(configuration, "Production"));
        }
        else
        {
            Assert.ThrowsExactly<FormatException>(() => new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build());
        }
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = File.ReadAllBytes(fixture.Settings);
        var environmentName = "Database__ConnectionString";
        var originalEnvironment = Environment.GetEnvironmentVariable(environmentName);
        try
        {
            Environment.SetEnvironmentVariable(environmentName, "Server=override.invalid;Password=credential-probe");
            foreach (var profile in new[] { "development", "production" })
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var result = await CodeGenerationCli.RunAsync(
                    ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
                Assert.AreEqual(valid ? 0 : 1, result, output.ToString());
                StringAssert.Contains(output.ToString(), valid
                    ? "DIAG_CONNECTION_CONFIGURED ok" : "DIAG_APPSETTINGS_INVALID error");
                Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.OrdinalIgnoreCase));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, originalEnvironment);
        }
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
            new { Database = new { ConnectionName = connectionName, MySqlGuidStorageMode = "Binary16" } }));
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
            Database = new { ConnectionName = connectionName, MySqlGuidStorageMode = "Binary16" },
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
            {"Database":{"MySqlGuidStorageMode":"Binary16"},
             "ConnectionStrings":{"fullnet":"Server=example.invalid;Password=credential-probe"},
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
              "database:MySqlGuidStorageMode":1,
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
        using var fixture = new DiagnoseWorkspace("""{"Database":{"MySqlGuidStorageMode":"Binary16"}}""");
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

    [TestMethod]
    [DataRow("\"credential-probe\"", false)]
    [DataRow("2", false)]
    [DataRow("-1", false)]
    [DataRow("true", false)]
    [DataRow("\"\"", false)]
    [DataRow("\"SqlServer\"", true)]
    [DataRow("\"mysql\"", true)]
    [DataRow("0", true)]
    [DataRow("1", true)]
    [DataRow("null", true)]
    [DataRow("\"1\"", true)]
    [DataRow("\" SqlServer \"", true)]
    [DataRow("\"SqlServer, MySql\"", true)]
    [DataRow("2147483648", false)]
    [DataRow("1.0", false)]
    [DataRow("[]", false)]
    [DataRow("{}", true)]
    public async Task Provider_diagnosis_agrees_with_runtime_binding_and_validation(string value, bool valid)
    {
        var configuration = "{\"Database\":{\"Provider\":" + value
            + "},\"ConnectionStrings\":{\"fullnet\":\"Server=example.invalid\"}}";
        var runtimeConfiguration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build();
        var services = new ServiceCollection();
        services.AddFullNetDapper(runtimeConfiguration, "Development");
        using var runtime = services.BuildServiceProvider();
        var runtimeValid = true;
        try
        {
            _ = runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OptionsValidationException)
        {
            runtimeValid = false;
        }
        Assert.AreEqual(valid, runtimeValid, "真实 Dapper Options 绑定与校验前提不符。");

        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var before = File.ReadAllBytes(fixture.Settings);
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root], output, error);

        Assert.AreEqual(valid ? 0 : 1, result);
        Assert.AreEqual(!valid, output.ToString().Contains("DIAG_DATABASE_PROVIDER_INVALID error", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
    }

    [TestMethod]
    [DataRow("profile", "development", true)]
    [DataRow("profile", "production", true)]
    [DataRow("secrets", "development", true)]
    [DataRow("secrets", "production", false)]
    [DataRow("environment", "development", true)]
    [DataRow("environment", "production", true)]
    public async Task Invalid_provider_uses_the_selected_runtime_configuration_source(
        string source, string profile, bool effective)
    {
        using var fixture = new DiagnoseWorkspace("""{"Database":{"Provider":"SqlServer"}}""");
        const string invalid = "credential-probe";
        if (source == "profile")
        {
            File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{(profile == "development" ? "Development" : "Production")}.json"),
                """{"dAtAbAsE:pRoViDeR":"credential-probe"}""");
        }
        if (source == "secrets")
        {
            fixture.AddStandaloneUserSecrets("""{"Database:Provider":"credential-probe"}""", "App.Host.Api");
        }
        var original = Environment.GetEnvironmentVariable("Database__Provider");
        try
        {
            Environment.SetEnvironmentVariable("Database__Provider", source == "environment" ? invalid : null);
            using var output = new StringWriter();
            using var error = new StringWriter();
            await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(effective, output.ToString().Contains("DIAG_DATABASE_PROVIDER_INVALID error", StringComparison.Ordinal));
            Assert.IsFalse((output.ToString() + error).Contains(invalid, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__Provider", original);
        }
    }

    [TestMethod]
    [DataRow("profile")]
    [DataRow("secrets")]
    [DataRow("environment")]
    public async Task Valid_provider_override_masks_invalid_lower_priority_value(string source)
    {
        using var fixture = new DiagnoseWorkspace("""{"Database":{"Provider":"credential-probe"}}""");
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.Development.json"),
            source == "profile" ? """{"Database:Provider":1}""" : """{"Database:Provider":"credential-probe"}""");
        if (source is "secrets" or "environment")
        {
            fixture.AddStandaloneUserSecrets(source == "secrets"
                ? """{"Database:Provider":0}""" : """{"Database:Provider":"credential-probe"}""", "App.Host.Api");
        }
        var original = Environment.GetEnvironmentVariable("Database__Provider");
        try
        {
            Environment.SetEnvironmentVariable("Database__Provider", source == "environment" ? "MySql" : null);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root], output, error);
            Assert.AreEqual(0, result);
            Assert.IsFalse(output.ToString().Contains("DIAG_DATABASE_PROVIDER_INVALID", StringComparison.Ordinal));
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Database__Provider", original);
        }
    }

    [TestMethod]
    [DataRow("0", false)]
    [DataRow("-1", false)]
    [DataRow("2147483648", false)]
    [DataRow("1.0", false)]
    [DataRow("true", false)]
    [DataRow("\"credential-probe\"", false)]
    [DataRow("\"\"", false)]
    [DataRow("[]", false)]
    [DataRow("null", false)]
    [DataRow("{}", false)]
    [DataRow("missing", true)]
    [DataRow("1", true)]
    [DataRow("2147483647", true)]
    [DataRow("\" +30 \"", true)]
    [DataRow("\"0x1\"", true)]
    [DataRow("\"#1\"", true)]
    [DataRow("\"&h1\"", true)]
    [DataRow("\"0xffffffff\"", false)]
    public async Task Database_options_timeout_matches_runtime_binding_and_positive_constraint(string value, bool valid)
    {
        var timeout = value == "missing" ? string.Empty : "\"CommandTimeoutSeconds\":" + value;
        var configuration = "{\"Database\":{" + timeout
            + "},\"ConnectionStrings\":{\"fullnet\":\"Server=example.invalid\"}}";
        Assert.AreEqual(valid, RuntimeDatabaseOptionsAreValid(configuration, "Development"));
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root], output, error);
        Assert.AreEqual(valid ? 0 : 1, result, output.ToString());
        Assert.AreEqual(!valid, output.ToString().Contains("DIAG_DATABASE_TIMEOUT_INVALID error", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("development", "MySql", "missing", true)]
    [DataRow("development", "MySql", "null", true)]
    [DataRow("development", "MySql", "0", true)]
    [DataRow("development", "MySql", "\"binary16\"", true)]
    [DataRow("development", "MySql", "2", false)]
    [DataRow("development", "MySql", "\"credential-probe\"", false)]
    [DataRow("development", "MySql", "\"\"", false)]
    [DataRow("production", "MySql", "missing", false)]
    [DataRow("production", "MySql", "null", false)]
    [DataRow("production", "MySql", "0", false)]
    [DataRow("production", "MySql", "1", true)]
    [DataRow("production", "MySql", "\"Binary16\"", true)]
    [DataRow("production", "SqlServer", "missing", false)]
    [DataRow("production", "SqlServer", "null", false)]
    [DataRow("production", "SqlServer", "0", true)]
    [DataRow("production", "SqlServer", "\"\"", false)]
    public async Task Database_options_guid_storage_matches_runtime_environment_gate(
        string profile, string provider, string value, bool valid)
    {
        var mode = value == "missing" ? string.Empty : ",\"MySqlGuidStorageMode\":" + value;
        var configuration = "{\"Database\":{\"Provider\":\"" + provider + "\"" + mode
            + "},\"ConnectionStrings\":{\"fullnet\":\"Server=example.invalid\"}}";
        Assert.AreEqual(valid, RuntimeDatabaseOptionsAreValid(configuration,
            profile == "production" ? "Production" : "Development"));
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        Assert.AreEqual(valid ? 0 : 1, result, output.ToString());
        Assert.AreEqual(!valid, output.ToString().Contains("DIAG_DATABASE_GUID_STORAGE_INVALID error", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("CommandTimeoutSeconds", "profile", true)]
    [DataRow("CommandTimeoutSeconds", "profile", false)]
    [DataRow("CommandTimeoutSeconds", "secrets", true)]
    [DataRow("CommandTimeoutSeconds", "secrets", false)]
    [DataRow("CommandTimeoutSeconds", "environment", true)]
    [DataRow("CommandTimeoutSeconds", "environment", false)]
    [DataRow("MySqlGuidStorageMode", "profile", true)]
    [DataRow("MySqlGuidStorageMode", "profile", false)]
    [DataRow("MySqlGuidStorageMode", "secrets", true)]
    [DataRow("MySqlGuidStorageMode", "secrets", false)]
    [DataRow("MySqlGuidStorageMode", "environment", true)]
    [DataRow("MySqlGuidStorageMode", "environment", false)]
    public async Task Database_options_respect_configuration_priority_and_redact_values(
        string field, string source, bool validOverride)
    {
        var baseline = new Dictionary<string, object?>
        {
            ["Provider"] = "MySql", ["CommandTimeoutSeconds"] = 30, ["MySqlGuidStorageMode"] = "Binary16",
        };
        baseline[field] = validOverride ? "credential-probe" : baseline[field];
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(new
        {
            Database = baseline, ConnectionStrings = new { fullnet = "Server=example.invalid" },
        }));
        // 高优先级 null 超时必须拒绝，不能恢复基础 30 秒；合法数字应覆盖低层错误值。
        object? overrideValue = validOverride ? 1 : field == "CommandTimeoutSeconds" ? null : "credential-probe";
        var configurationOverride = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [$"database:{field}"] = overrideValue,
        });
        var profile = source == "secrets" ? "development" : "production";
        var profilePath = Path.Combine(fixture.Root, $"appsettings.{(profile == "production" ? "Production" : "Development")}.json");
        if (source == "profile") File.WriteAllText(profilePath, configurationOverride);
        if (source == "secrets") fixture.AddStandaloneUserSecrets(configurationOverride, "App.Host.Api");
        var key = "Database__" + field;
        var original = Environment.GetEnvironmentVariable(key);
        var before = File.ReadAllBytes(fixture.Settings);
        try
        {
            Environment.SetEnvironmentVariable(key, source == "environment"
                ? validOverride ? "1" : "credential-probe" : null);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(validOverride ? 0 : 1, result, output.ToString());
            StringAssert.Contains(output.ToString(), validOverride ? "DIAG_CONNECTION_CONFIGURED ok"
                : field == "CommandTimeoutSeconds" ? "DIAG_DATABASE_TIMEOUT_INVALID error" : "DIAG_DATABASE_GUID_STORAGE_INVALID error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    public async Task Database_options_production_ignores_development_user_secrets()
    {
        using var fixture = new DiagnoseWorkspace("""
            {"Database":{"Provider":"MySql","MySqlGuidStorageMode":"Binary16","CommandTimeoutSeconds":30},
             "ConnectionStrings":{"fullnet":"Server=example.invalid"}}
            """);
        fixture.AddStandaloneUserSecrets("""{"Database:MySqlGuidStorageMode":"credential-probe","Database:CommandTimeoutSeconds":0}""", "App.Host.Api");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);
        Assert.AreEqual(0, result, output.ToString());
        Assert.IsFalse(output.ToString().Contains("DIAG_DATABASE_GUID_STORAGE_INVALID", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("DIAG_DATABASE_TIMEOUT_INVALID", StringComparison.Ordinal));
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("\"Server=direct.invalid;Password=credential-probe\"", false, true)]
    [DataRow("\"Server=direct.invalid;Password=credential-probe\"", true, true)]
    [DataRow("\"<your-connection>\"", true, false)]
    [DataRow("\"CHANGEME\"", true, false)]
    [DataRow("\"YOUR_CONNECTION_STRING\"", true, false)]
    [DataRow("\" \"", true, true)]
    [DataRow("\"\"", true, true)]
    [DataRow("null", true, true)]
    [DataRow("{}", true, true)]
    [DataRow("[]", true, true)]
    [DataRow("missing", true, true)]
    [DataRow("\" \"", false, false)]
    [DataRow("\"\"", false, false)]
    [DataRow("null", false, false)]
    [DataRow("{}", false, false)]
    [DataRow("[]", false, false)]
    public async Task Direct_connection_matches_runtime_selection_and_placeholder_boundary(
        string value, bool hasNamedConnection, bool configured)
    {
        var direct = value == "missing" ? string.Empty : ",\"ConnectionString\":" + value;
        var configuration = "{\"Database\":{\"MySqlGuidStorageMode\":\"Binary16\"" + direct + "}"
            + (hasNamedConnection ? ",\"ConnectionStrings\":{\"fullnet\":\"Server=named.invalid;Password=credential-probe\"}" : string.Empty) + "}";
        var hasDirect = value.StartsWith("\"Server=", StringComparison.Ordinal)
            || value is "\"<your-connection>\"" or "\"CHANGEME\"" or "\"YOUR_CONNECTION_STRING\"";
        Assert.AreEqual(hasDirect || hasNamedConnection, RuntimeDatabaseOptionsAreValid(configuration, "Production"));
        if (hasDirect || hasNamedConnection)
        {
            Assert.AreEqual(hasDirect ? JsonSerializer.Deserialize<string>(value)
                : "Server=named.invalid;Password=credential-probe", ReadRuntimeDatabaseConnection(configuration));
        }
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = File.ReadAllBytes(fixture.Settings);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);
        Assert.AreEqual(configured ? 0 : 1, result, output.ToString());
        StringAssert.Contains(output.ToString(), configured ? "DIAG_CONNECTION_CONFIGURED ok" : "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
    }

    [TestMethod]
    [DataRow("profile", "configured")]
    [DataRow("profile", "placeholder")]
    [DataRow("profile", "null")]
    [DataRow("profile", "blank")]
    [DataRow("secrets", "configured")]
    [DataRow("secrets", "placeholder")]
    [DataRow("secrets", "null")]
    [DataRow("secrets", "blank")]
    [DataRow("environment", "configured")]
    [DataRow("environment", "placeholder")]
    [DataRow("environment", "null")]
    [DataRow("environment", "blank")]
    public async Task Direct_connection_respects_source_priority_and_empty_value_fallback(string source, string kind)
    {
        const string configuration = """
            {"Database":{"ConnectionString":"Server=base.invalid;Password=credential-probe","MySqlGuidStorageMode":"Binary16"},
             "ConnectionStrings":{"fullnet":"Server=named.invalid;Password=credential-probe"}}
            """;
        var value = kind == "configured" ? "Server=override.invalid;Password=credential-probe"
            : kind == "placeholder" ? "CHANGEME" : kind == "blank" ? " " : null;
        var overlay = JsonSerializer.Serialize(new Dictionary<string, string?> { ["database:connectionstring"] = value });
        using var fixture = new DiagnoseWorkspace(configuration);
        var profilePath = Path.Combine(fixture.Root, "appsettings.Development.json");
        // 给高层来源设置相反的低层值，证明逐键覆盖先于直配/命名连接的选择。
        var lower = """{"Database:ConnectionString":"CHANGEME"}""";
        File.WriteAllText(profilePath, source == "profile" ? overlay : lower);
        if (source == "secrets") fixture.AddStandaloneUserSecrets(overlay, "App.Host.Api");
        if (source == "environment") fixture.AddStandaloneUserSecrets(lower, "App.Host.Api");
        var key = "Database__ConnectionString";
        var original = Environment.GetEnvironmentVariable(key);
        var before = File.ReadAllBytes(fixture.Settings);
        var profileBefore = File.ReadAllBytes(profilePath);
        try
        {
            Environment.SetEnvironmentVariable(key, source == "environment" ? value ?? string.Empty : null);
            Assert.AreEqual(kind is "null" or "blank" ? "Server=named.invalid;Password=credential-probe" : value,
                ReadRuntimeDatabaseConnection(configuration, overlay));
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root], output, error);
            Assert.AreEqual(0, result, output.ToString());
            StringAssert.Contains(output.ToString(), kind == "placeholder"
                ? "DIAG_CONNECTION_PLACEHOLDER warn" : "DIAG_CONNECTION_CONFIGURED ok");
            Assert.AreEqual(kind != "placeholder", output.ToString().Contains("DIAG_CONNECTION_CONFIGURED ok", StringComparison.Ordinal));
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
            CollectionAssert.AreEqual(profileBefore, File.ReadAllBytes(profilePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Direct_connection_production_ignores_development_secrets(bool configuredBase)
    {
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(new
        {
            Database = new { ConnectionString = configuredBase ? "Server=base.invalid;Password=credential-probe" : "CHANGEME", MySqlGuidStorageMode = "Binary16" },
            ConnectionStrings = new { fullnet = "Server=named.invalid" },
        }));
        fixture.AddStandaloneUserSecrets(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["Database:ConnectionString"] = configuredBase ? "CHANGEME" : "Server=secret.invalid;Password=credential-probe",
        }), "App.Host.Api");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);
        Assert.AreEqual(configuredBase ? 0 : 1, result, output.ToString());
        StringAssert.Contains(output.ToString(), configuredBase ? "DIAG_CONNECTION_CONFIGURED ok" : "DIAG_CONNECTION_MISSING error");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("\"\"")]
    [DataRow("\"credential-probe\"")]
    public async Task Direct_connection_does_not_require_unused_connection_name(string connectionName)
    {
        var configuration = "{\"Database\":{\"ConnectionString\":\"Server=direct.invalid;Password=credential-probe\","
            + "\"MySqlGuidStorageMode\":\"Binary16\",\"ConnectionName\":" + connectionName + "}}";
        Assert.AreEqual("Server=direct.invalid;Password=credential-probe", ReadRuntimeDatabaseConnection(configuration));
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);
        Assert.AreEqual(0, result, output.ToString());
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Direct_connection_invalid_development_secrets_remains_error()
    {
        using var fixture = new DiagnoseWorkspace("""{"Database":{"ConnectionString":"Server=direct.invalid;Password=credential-probe"}}""");
        fixture.AddStandaloneUserSecrets("{invalid-credential-probe", "App.Host.Api");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root], output, error);
        Assert.AreEqual(1, result, output.ToString());
        StringAssert.Contains(output.ToString(), "DIAG_USER_SECRETS_INVALID error");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Provider", false, "development")]
    [DataRow("Provider", true, "development")]
    [DataRow("CommandTimeoutSeconds", false, "development")]
    [DataRow("CommandTimeoutSeconds", true, "development")]
    [DataRow("MySqlGuidStorageMode", false, "development")]
    [DataRow("MySqlGuidStorageMode", true, "development")]
    [DataRow("ConnectionString", false, "development")]
    [DataRow("ConnectionString", true, "development")]
    [DataRow("ConnectionName", false, "development")]
    [DataRow("ConnectionName", true, "development")]
    [DataRow("Provider", false, "production")]
    [DataRow("Provider", true, "production")]
    [DataRow("CommandTimeoutSeconds", false, "production")]
    [DataRow("CommandTimeoutSeconds", true, "production")]
    [DataRow("MySqlGuidStorageMode", false, "production")]
    [DataRow("MySqlGuidStorageMode", true, "production")]
    [DataRow("ConnectionString", false, "production")]
    [DataRow("ConnectionString", true, "production")]
    [DataRow("ConnectionName", false, "production")]
    [DataRow("ConnectionName", true, "production")]
    public async Task Database_structure_matches_real_options_binding(string field, bool array, string profile)
    {
        var database = new Dictionary<string, object?>
        {
            ["Provider"] = "MySql", ["CommandTimeoutSeconds"] = 30,
            ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionName"] = "fullnet",
        };
        database[field] = array ? new[] { "credential-probe" } : new Dictionary<string, string> { ["Probe"] = "credential-probe" };
        var configuration = JsonSerializer.Serialize(new
        {
            Database = database, ConnectionStrings = new { fullnet = "Server=named.invalid;Password=credential-probe" },
        });
        var runtimeValid = RuntimeDatabaseOptionsAreValid(configuration, profile == "production" ? "Production" : "Development");
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var before = File.ReadAllBytes(fixture.Settings);
        var result = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        Assert.AreEqual(runtimeValid ? 0 : 1, result, $"真实 Options 是否有效：{runtimeValid}；{output}");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
    }

    [TestMethod]
    [DataRow("CommandTimeoutSeconds", "base", "child", false)]
    [DataRow("CommandTimeoutSeconds", "profile", "child", false)]
    [DataRow("CommandTimeoutSeconds", "secrets", "child", false)]
    [DataRow("CommandTimeoutSeconds", "environment", "child", false)]
    [DataRow("CommandTimeoutSeconds", "base", "null", false)]
    [DataRow("CommandTimeoutSeconds", "profile", "null", false)]
    [DataRow("CommandTimeoutSeconds", "secrets", "null", false)]
    [DataRow("CommandTimeoutSeconds", "base", "scalar", true)]
    [DataRow("CommandTimeoutSeconds", "profile", "scalar", true)]
    [DataRow("CommandTimeoutSeconds", "secrets", "scalar", true)]
    [DataRow("CommandTimeoutSeconds", "environment", "scalar", true)]
    [DataRow("ConnectionName", "base", "child", false)]
    [DataRow("ConnectionName", "profile", "child", false)]
    [DataRow("ConnectionName", "secrets", "child", false)]
    [DataRow("ConnectionName", "environment", "child", false)]
    [DataRow("ConnectionName", "base", "null", false)]
    [DataRow("ConnectionName", "profile", "null", false)]
    [DataRow("ConnectionName", "secrets", "null", false)]
    [DataRow("ConnectionName", "base", "scalar", true)]
    [DataRow("ConnectionName", "profile", "scalar", true)]
    [DataRow("ConnectionName", "secrets", "scalar", true)]
    [DataRow("ConnectionName", "environment", "scalar", true)]
    public async Task Database_structure_null_and_children_follow_merged_binding(
        string field, string source, string shape, bool valid)
    {
        var database = new Dictionary<string, object?>
        {
            ["Provider"] = "MySql", ["CommandTimeoutSeconds"] = 30,
            ["MySqlGuidStorageMode"] = "Binary16", ["ConnectionName"] = "fullnet",
        };
        database[field] = null;
        var root = new Dictionary<string, object?>
        {
            ["Database"] = database,
            ["ConnectionStrings"] = new { fullnet = "Server=named.invalid;Password=credential-probe" },
        };
        var configurationOverride = new Dictionary<string, object?>();
        if (shape == "child") configurationOverride[$"database:{field}:Probe"] = "credential-probe";
        if (shape == "scalar") configurationOverride[$"database:{field}"] = field == "ConnectionName" ? "fullnet" : 1;
        if (source == "base")
        {
            if (shape == "scalar") database[field] = configurationOverride.Values.Single();
            else foreach (var entry in configurationOverride) root[entry.Key] = entry.Value;
        }
        var configuration = JsonSerializer.Serialize(root);
        var overlay = JsonSerializer.Serialize(configurationOverride);
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (source != "base") builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        var services = new ServiceCollection();
        services.AddFullNetDapper(builder.Build(), "Development");
        using (var runtime = services.BuildServiceProvider())
        {
            if (valid)
            {
                var options = runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                Assert.AreEqual("fullnet", options.ConnectionName);
                Assert.AreEqual(shape == "scalar" && field == "CommandTimeoutSeconds" ? 1 : 30, options.CommandTimeoutSeconds);
            }
            else
            {
                Assert.ThrowsExactly<OptionsValidationException>(() => { _ = runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value; });
            }
        }
        using var fixture = new DiagnoseWorkspace(configuration);
        var profilePath = Path.Combine(fixture.Root, "appsettings.Development.json");
        if (source == "profile") File.WriteAllText(profilePath, overlay);
        if (source == "secrets") fixture.AddStandaloneUserSecrets(overlay, "App.Host.Api");
        var key = "Database__" + field + (shape == "child" ? "__Probe" : string.Empty);
        var original = Environment.GetEnvironmentVariable(key);
        var before = File.ReadAllBytes(fixture.Settings);
        try
        {
            Environment.SetEnvironmentVariable(key, source == "environment" ? configurationOverride.Values.Single()?.ToString() : null);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root], output, error);
            Assert.AreEqual(valid ? 0 : 1, result, output.ToString());
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    [DataRow("42", "42")]
    [DataRow("true", "True")]
    public async Task Database_structure_connection_name_scalar_conversion_matches_runtime(string value, string name)
    {
        var configuration = "{\"Database\":{\"ConnectionName\":" + value
            + "},\"ConnectionStrings\":{" + JsonSerializer.Serialize(name) + ":\"Server=named.invalid;Password=credential-probe\"}}";
        Assert.IsTrue(RuntimeDatabaseOptionsAreValid(configuration, "Development"));
        using var fixture = new DiagnoseWorkspace(configuration);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root], output, error);
        Assert.AreEqual(0, result, output.ToString());
        StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
    }

    private static string ReadRuntimeDatabaseConnection(string configuration, string? overlay = null)
    {
        var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        if (overlay is not null) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
        var services = new ServiceCollection();
        services.AddFullNetDapper(builder.Build(), "Production");
        using var runtime = services.BuildServiceProvider();
        return runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
    }

    private static bool RuntimeDatabaseOptionsAreValid(string configuration, string environment)
    {
        var runtimeConfiguration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build();
        var services = new ServiceCollection();
        services.AddFullNetDapper(runtimeConfiguration, environment);
        using var runtime = services.BuildServiceProvider();
        try
        {
            _ = runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OptionsValidationException)
        {
            return false;
        }
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
