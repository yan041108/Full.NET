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
    private string? originalTokenEndpoints;

    [TestInitialize]
    public void Scope_existing_configuration_tests_to_their_database_and_secret_checks()
    {
        // 本类测试数据库与通用秘密，签名准入由独立真实 Options 对照集覆盖。
        originalTokenEndpoints = Environment.GetEnvironmentVariable("Identity__EnableTokenEndpoints");
        Environment.SetEnvironmentVariable("Identity__EnableTokenEndpoints", "false");
    }

    [TestCleanup]
    public void Restore_token_endpoint_configuration() =>
        Environment.SetEnvironmentVariable("Identity__EnableTokenEndpoints", originalTokenEndpoints);

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
    [DataRow("MYSQLCONNSTR_", "development", false, "named")]
    [DataRow("SQLCONNSTR_", "development", false, "named")]
    [DataRow("SQLAZURECONNSTR_", "development", false, "named")]
    [DataRow("CUSTOMCONNSTR_", "development", false, "named")]
    [DataRow("MYSQLCONNSTR_", "production", false, "named")]
    [DataRow("SQLCONNSTR_", "production", false, "named")]
    [DataRow("SQLAZURECONNSTR_", "production", false, "named")]
    [DataRow("CUSTOMCONNSTR_", "production", false, "named")]
    [DataRow("MYSQLCONNSTR_", "development", true, "named")]
    [DataRow("SQLCONNSTR_", "development", true, "named")]
    [DataRow("SQLAZURECONNSTR_", "development", true, "named")]
    [DataRow("CUSTOMCONNSTR_", "development", true, "named")]
    [DataRow("MYSQLCONNSTR_", "production", true, "named")]
    [DataRow("SQLCONNSTR_", "production", true, "named")]
    [DataRow("SQLAZURECONNSTR_", "production", true, "named")]
    [DataRow("CUSTOMCONNSTR_", "production", true, "named")]
    [DataRow("mysqlconnstr_", "production", false, "nested")]
    [DataRow("sQlCoNnStR_", "production", false, "nested")]
    [DataRow("sqlazureconnstr_", "production", false, "nested")]
    [DataRow("customconnstr_", "production", false, "nested")]
    [DataRow("MYSQLCONNSTR_", "production", true, "direct")]
    [DataRow("SQLCONNSTR_", "production", true, "direct")]
    [DataRow("SQLAZURECONNSTR_", "production", true, "direct")]
    [DataRow("CUSTOMCONNSTR_", "production", true, "direct")]
    [DataRow("MYSQLCONNSTR_", "production", false, "metadata")]
    [DataRow("SQLCONNSTR_", "production", false, "metadata")]
    [DataRow("SQLAZURECONNSTR_", "production", false, "metadata")]
    [DataRow("CUSTOMCONNSTR_", "production", false, "metadata")]
    [DataRow("MYSQLCONNSTR_", "production", true, "aliases")]
    [DataRow("SQLCONNSTR_", "production", true, "aliases")]
    [DataRow("SQLAZURECONNSTR_", "production", true, "aliases")]
    [DataRow("CUSTOMCONNSTR_", "production", true, "aliases")]
    public async Task Special_connection_environment_prefixes_match_runtime_provider(
        string prefix, string profile, bool placeholder, string mode)
    {
        var connectionName = $"diagnose_{Guid.NewGuid():N}" + (mode == "nested" ? ":primary" : string.Empty);
        var environmentKey = prefix + connectionName.Replace(":", "__", StringComparison.Ordinal);
        if (mode == "metadata") connectionName += "_ProviderName";
        var aliasKey = "ConnectionStrings__" + connectionName.Replace(":", "__", StringComparison.Ordinal);
        const string validConnection = "Server=example.invalid;Password=credential-probe";
        const string directConnection = "Server=direct.invalid;Password=credential-probe";
        var environmentConnection = placeholder ? "CHANGEME" : validConnection;
        var lowerConnection = placeholder ? validConnection : "CHANGEME";
        var direct = mode == "direct";
        var configuration = JsonSerializer.Serialize(new
        {
            Database = new { ConnectionName = connectionName, MySqlGuidStorageMode = "Binary16",
                ConnectionString = direct ? directConnection : null },
            ConnectionStrings = new Dictionary<string, string> { [connectionName] = lowerConnection },
        });
        var overlay = JsonSerializer.Serialize(new
        {
            ConnectionStrings = new Dictionary<string, string> { [connectionName] = lowerConnection },
        });
        using var fixture = new DiagnoseWorkspace(configuration);
        var profileName = profile == "development" ? "Development" : "Production";
        File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{profileName}.json"), overlay, new UTF8Encoding(false));
        if (profile == "development") fixture.AddStandaloneUserSecrets(overlay, "App.Host.Api");
        var beforeFiles = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var originalEnvironment = Environment.GetEnvironmentVariable(environmentKey);
        var originalAlias = Environment.GetEnvironmentVariable(aliasKey);
        try
        {
            Environment.SetEnvironmentVariable(environmentKey, environmentConnection);
            if (mode == "aliases") Environment.SetEnvironmentVariable(aliasKey, validConnection);
            // 使用真实提供程序和 Dapper 绑定作对照，不能用诊断自己的映射证明映射正确。
            var builder = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)))
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
            if (profile == "development") builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
            var runtimeConfiguration = builder.AddEnvironmentVariables().Build();
            var expectedConnection = mode == "metadata" ? prefix switch
            {
                "MYSQLCONNSTR_" => "MySql.Data.MySqlClient",
                "CUSTOMCONNSTR_" => lowerConnection,
                _ => "System.Data.SqlClient",
            } : environmentConnection;
            var runtimeConnection = runtimeConfiguration.GetConnectionString(connectionName);
            if (mode == "aliases")
            {
                // 同路径多别名的宿主枚举顺序不固定；诊断保持任一占位值都拒绝的既有保护。
                CollectionAssert.Contains(new[] { environmentConnection, validConnection }, runtimeConnection);
            }
            else
            {
                Assert.AreEqual(expectedConnection, runtimeConnection);
            }
            var services = new ServiceCollection();
            services.AddFullNetDapper(runtimeConfiguration, profileName);
            using var runtime = services.BuildServiceProvider();
            Assert.AreEqual(direct ? directConnection : runtimeConnection,
                runtime.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);

            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            var configured = direct || (!placeholder && expectedConnection != "CHANGEME");
            Assert.AreEqual(!configured && profile == "production" ? 1 : 0, result, output.ToString());
            StringAssert.Contains(output.ToString(), configured ? "DIAG_CONNECTION_CONFIGURED ok"
                : profile == "production" ? "DIAG_CONNECTION_MISSING error" : "DIAG_CONNECTION_PLACEHOLDER warn");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse((output.ToString() + error).Contains(connectionName, StringComparison.OrdinalIgnoreCase));
            CollectionAssert.AreEquivalent(beforeFiles.Keys.ToArray(),
                Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
            foreach (var (path, bytes) in beforeFiles) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, originalEnvironment);
            if (mode == "aliases") Environment.SetEnvironmentVariable(aliasKey, originalAlias);
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

    [TestMethod]
    [DataRow("nested", true)]
    [DataRow("lowercase", true)]
    [DataRow("flat", true)]
    [DataRow("flat-case", true)]
    [DataRow("colon-key", true)]
    [DataRow("nested-colon", true)]
    [DataRow("array", true)]
    [DataRow("empty-leaf", true)]
    [DataRow("empty-root", false)]
    [DataRow("other", false)]
    [DataRow("empty-object-overwrite", false)]
    [DataRow("empty-array-overwrite", false)]
    [DataRow("children-below", true)]
    public async Task Base_named_connection_layout_matches_runtime_configuration(string layout, bool configured)
    {
        const string credential = "Server=named.invalid;Password=credential-probe";
        var name = "diagnose_" + Guid.NewGuid().ToString("N");
        var connectionName = layout is "colon-key" or "nested-colon" ? name + ":read"
            : layout == "array" ? name + ":0" : layout == "empty-leaf" ? name + ":" : name;
        object namedValue = layout switch
        {
            "nested-colon" => new Dictionary<string, object> { [name] = new { read = credential } },
            "array" => new Dictionary<string, object> { [name] = new[] { credential } },
            "empty-leaf" => new Dictionary<string, object> { [name] = new Dictionary<string, string> { [""] = credential } },
            "other" => new Dictionary<string, string> { [name + "_other"] = credential },
            _ => new Dictionary<string, string> { [connectionName] = credential },
        };
        var settings = new Dictionary<string, object>
        {
            ["Database"] = new { Provider = "MySql", MySqlGuidStorageMode = "Binary16", ConnectionName = connectionName },
        };
        if (layout is "flat" or "flat-case")
        {
            settings[layout == "flat-case" ? ("connectionstrings:" + connectionName).ToUpperInvariant()
                : "ConnectionStrings:" + connectionName] = credential;
        }
        else if (layout == "empty-root")
        {
            settings[""] = new { ConnectionStrings = namedValue };
        }
        else
        {
            settings[layout == "lowercase" ? "connectionstrings" : "ConnectionStrings"] = namedValue;
        }
        if (layout is "empty-object-overwrite" or "empty-array-overwrite" or "children-below")
        {
            settings["connectionstrings:" + connectionName] = layout == "empty-array-overwrite" ? Array.Empty<string>()
                : layout == "empty-object-overwrite" ? new Dictionary<string, string>() : new { Probe = credential };
        }
        var configuration = JsonSerializer.Serialize(settings);
        Assert.AreEqual(configured, RuntimeDatabaseOptionsAreValid(configuration, "Production"));
        if (configured) Assert.AreEqual(credential, ReadRuntimeDatabaseConnection(configuration));
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = File.ReadAllBytes(fixture.Settings);
        foreach (var profile in new[] { "development", "production" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(configured || profile == "development" ? 0 : 1, result, output.ToString());
            StringAssert.Contains(output.ToString(), configured ? "DIAG_CONNECTION_CONFIGURED ok"
                : profile == "production" ? "DIAG_CONNECTION_MISSING error" : "DIAG_CONNECTION_PLACEHOLDER warn");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
    }

    [TestMethod]
    [DataRow("profile", "null")]
    [DataRow("profile", "blank")]
    [DataRow("profile", "placeholder")]
    [DataRow("profile", "valid")]
    [DataRow("secrets", "null")]
    [DataRow("secrets", "blank")]
    [DataRow("secrets", "placeholder")]
    [DataRow("secrets", "valid")]
    [DataRow("environment", "null")]
    [DataRow("environment", "blank")]
    [DataRow("environment", "placeholder")]
    [DataRow("environment", "valid")]
    public async Task Base_named_connection_overrides_preserve_precedence_and_explicit_empty(string source, string shape)
    {
        var name = "diagnose_" + Guid.NewGuid().ToString("N");
        const string baseCredential = "Server=base.invalid;Password=credential-probe";
        var configuration = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Database"] = new { Provider = "MySql", MySqlGuidStorageMode = "Binary16", ConnectionName = name },
            ["connectionstrings:" + name.ToUpperInvariant()] = baseCredential,
        });
        var value = shape switch
        {
            "null" => null, "blank" => " ", "placeholder" => "CHANGEME",
            _ => "Server=override.invalid;Password=credential-probe",
        };
        var overlay = JsonSerializer.Serialize(new Dictionary<string, string?> { ["ConnectionStrings:" + name] = value });
        using var fixture = new DiagnoseWorkspace(configuration);
        var paths = new List<string> { fixture.Settings };
        if (source == "profile")
        {
            foreach (var environment in new[] { "Development", "Production" })
            {
                var path = Path.Combine(fixture.Root, $"appsettings.{environment}.json");
                File.WriteAllText(path, overlay, new UTF8Encoding(false));
                paths.Add(path);
            }
        }
        if (source == "secrets") fixture.AddStandaloneUserSecrets(overlay, "App.Host.Api");
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        var key = "ConnectionStrings__" + name;
        var originalEnvironment = Environment.GetEnvironmentVariable(key);
        try
        {
            if (source == "environment") Environment.SetEnvironmentVariable(key, value);
            foreach (var profile in new[] { "development", "production" })
            {
                var usesBase = (source == "secrets" && profile == "production")
                    || (source == "environment" && value is null);
                var configured = usesBase || shape == "valid";
                // null 环境变量表示移除；JSON null 是显式覆盖，Production 不加载开发秘密。
                if (configured) Assert.AreEqual(usesBase ? baseCredential : value,
                    ReadRuntimeDatabaseConnection(configuration, usesBase ? null : overlay));
                using var output = new StringWriter();
                using var error = new StringWriter();
                var result = await CodeGenerationCli.RunAsync(
                    ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
                Assert.AreEqual(configured || profile == "development" ? 0 : 1, result, output.ToString());
                StringAssert.Contains(output.ToString(), configured ? "DIAG_CONNECTION_CONFIGURED ok"
                    : profile == "production" ? "DIAG_CONNECTION_MISSING error" : "DIAG_CONNECTION_PLACEHOLDER warn");
                Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
                foreach (var path in paths) CollectionAssert.AreEqual(before[path], File.ReadAllBytes(path));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, originalEnvironment);
        }
    }

    [TestMethod]
    [DataRow("flat", false)]
    [DataRow("flat", true)]
    [DataRow("nested", false)]
    [DataRow("nested", true)]
    public async Task Base_named_connection_nontext_values_do_not_count_as_credentials(string layout, bool boolean)
    {
        var name = "diagnose_" + Guid.NewGuid().ToString("N");
        object value = boolean ? true : 42;
        var configuration = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Database"] = new { MySqlGuidStorageMode = "Binary16", ConnectionName = name },
            [layout == "flat" ? "ConnectionStrings:" + name : "ConnectionStrings"] = layout == "flat" ? value
                : new Dictionary<string, object> { [name] = value },
        });
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = File.ReadAllBytes(fixture.Settings);
        foreach (var profile in new[] { "development", "production" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(layout == "nested" || profile == "production" ? 1 : 0, result);
            Assert.IsFalse(output.ToString().Contains("DIAG_CONNECTION_CONFIGURED", StringComparison.Ordinal));
            StringAssert.Contains(output.ToString(), layout == "nested" ? "DIAG_APPSETTINGS_INVALID error"
                : profile == "production" ? "DIAG_CONNECTION_MISSING error" : "DIAG_CONNECTION_PLACEHOLDER warn");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
    }

    [TestMethod]
    [DataRow("nested")]
    [DataRow("flat")]
    [DataRow("lowercase")]
    [DataRow("flat-case")]
    [DataRow("nested-null")]
    [DataRow("flat-null")]
    [DataRow("flat-blank")]
    [DataRow("empty-object-overwrite")]
    [DataRow("empty-array-overwrite")]
    [DataRow("children-below")]
    [DataRow("empty-root")]
    [DataRow("valid-flat")]
    [DataRow("valid-lowercase")]
    public async Task Base_secret_paths_match_real_configuration_values(string layout)
    {
        var settings = DiagnosticSecretSettings();
        foreach (var path in DiagnosticSecretPaths)
        {
            object? value = layout is "nested-null" or "flat-null" ? null
                : layout == "flat-blank" ? " " : layout.StartsWith("valid-", StringComparison.Ordinal)
                    || layout is "empty-object-overwrite" or "empty-array-overwrite" ? "credential-probe" : "CHANGEME";
            if (layout is "flat" or "flat-null" or "flat-blank" or "valid-flat" or "flat-case")
            {
                settings[layout == "flat-case" ? path.ToUpperInvariant() : path] = value;
            }
            else if (layout == "empty-root")
            {
                if (!settings.TryGetValue("", out var decoy)) settings[""] = decoy = new Dictionary<string, object?>();
                AddNestedSecret((Dictionary<string, object?>)decoy!, path, value);
            }
            else
            {
                AddNestedSecret(settings, layout is "lowercase" or "valid-lowercase" ? path.ToLowerInvariant() : path, value);
            }
            if (layout is "empty-object-overwrite" or "empty-array-overwrite" or "children-below")
            {
                settings[path.ToUpperInvariant()] = layout == "empty-array-overwrite" ? Array.Empty<string>()
                    : layout == "empty-object-overwrite" ? new Dictionary<string, string>() : new { Probe = "credential-probe" };
            }
        }
        var configuration = JsonSerializer.Serialize(settings);
        var runtime = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build();
        var runtimeCount = 0;
        foreach (var path in DiagnosticSecretPaths)
        {
            // 实际提供程序区分未声明路径与显式 null；非空子键不覆盖同路径标量。
            var present = runtime.Providers.Single().TryGet(path, out var value);
            if (present && (string.IsNullOrWhiteSpace(value) || value == "CHANGEME")) runtimeCount++;
        }
        Assert.AreEqual(layout is "empty-root" or "valid-flat" or "valid-lowercase" ? 0 : 3, runtimeCount);
        using var fixture = new DiagnoseWorkspace(configuration);
        foreach (var profile in new[] { "development", "production" })
            await AssertSecretDiagnosisAsync(fixture, profile, runtimeCount);
    }

    [TestMethod]
    [DataRow("profile", "null")]
    [DataRow("profile", "blank")]
    [DataRow("profile", "placeholder")]
    [DataRow("profile", "valid")]
    [DataRow("secrets", "null")]
    [DataRow("secrets", "blank")]
    [DataRow("secrets", "placeholder")]
    [DataRow("secrets", "valid")]
    [DataRow("environment", "null")]
    [DataRow("environment", "blank")]
    [DataRow("environment", "placeholder")]
    [DataRow("environment", "valid")]
    public async Task Base_secret_paths_preserve_override_precedence_and_explicit_empty(string source, string shape)
    {
        var settings = DiagnosticSecretSettings();
        foreach (var path in DiagnosticSecretPaths) settings[path.ToUpperInvariant()] = "CHANGEME";
        var configuration = JsonSerializer.Serialize(settings);
        var value = shape switch { "null" => null, "blank" => " ", "placeholder" => "CHANGEME", _ => "credential-probe" };
        var overlay = JsonSerializer.Serialize(DiagnosticSecretPaths.ToDictionary(path => path, _ => value));
        using var fixture = new DiagnoseWorkspace(configuration);
        if (source == "profile")
        {
            foreach (var environment in new[] { "Development", "Production" })
                File.WriteAllText(Path.Combine(fixture.Root, $"appsettings.{environment}.json"), overlay, new UTF8Encoding(false));
        }
        if (source == "secrets") fixture.AddStandaloneUserSecrets(overlay, "App.Host.Api");
        var originals = DiagnosticSecretPaths.ToDictionary(path => path.Replace(":", "__", StringComparison.Ordinal),
            Environment.GetEnvironmentVariable);
        try
        {
            if (source == "environment")
                foreach (var key in originals.Keys) Environment.SetEnvironmentVariable(key, value);
            foreach (var profile in new[] { "development", "production" })
            {
                var usesBase = (source == "secrets" && profile == "production") || (source == "environment" && value is null);
                var builder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
                if (!usesBase) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(overlay)));
                var runtime = builder.Build();
                foreach (var path in DiagnosticSecretPaths) Assert.AreEqual(usesBase ? "CHANGEME" : value, runtime[path]);
                await AssertSecretDiagnosisAsync(fixture, profile, usesBase || shape != "valid" ? 3 : 0);
            }
        }
        finally
        {
            foreach (var (key, original) in originals) Environment.SetEnvironmentVariable(key, original);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Base_secret_paths_flat_nontext_values_are_not_credentials(bool boolean)
    {
        var settings = DiagnosticSecretSettings();
        foreach (var path in DiagnosticSecretPaths) settings[path] = boolean ? true : 42;
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(settings));
        // JSON 非文本值保持秘密字段的失败关闭策略，不能因框架可转字符串便计为有效凭据。
        foreach (var profile in new[] { "development", "production" }) await AssertSecretDiagnosisAsync(fixture, profile, 3);
    }

    [TestMethod]
    [DataRow("Cache:RedisConnectionString", false)]
    [DataRow("Cache:RedisConnectionString", true)]
    [DataRow("Realtime:RedisBackplaneConnectionString", false)]
    [DataRow("Realtime:RedisBackplaneConnectionString", true)]
    [DataRow("FullNet:Cryptography:Sm2PrivateKeys:host-integration-signing", false)]
    [DataRow("FullNet:Cryptography:Sm2PrivateKeys:host-integration-signing", true)]
    public async Task Base_secret_paths_preserve_existing_nested_type_validation(string path, bool boolean)
    {
        var settings = DiagnosticSecretSettings();
        AddNestedSecret(settings, path, boolean ? true : 42);
        using var fixture = new DiagnoseWorkspace(JsonSerializer.Serialize(settings));
        var before = File.ReadAllBytes(fixture.Settings);
        foreach (var profile in new[] { "development", "production" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(1, result);
            StringAssert.Contains(output.ToString(), "DIAG_APPSETTINGS_INVALID error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("nested")]
    [DataRow("modules")]
    [DataRow("database")]
    [DataRow("connections")]
    [DataRow("nested-connections")]
    [DataRow("secrets")]
    [DataRow("empty-object")]
    [DataRow("empty-array")]
    [DataRow("empty-then-object")]
    [DataRow("secret-parent-empty")]
    [DataRow("connection-parent-empty")]
    [DataRow("case-split")]
    [DataRow("repeated-empty")]
    public async Task Repeated_json_sections_with_disjoint_paths_match_real_configuration(string shape)
    {
        var configuration = RepeatedSectionConfiguration(shape);
        Assert.IsTrue(RuntimeDatabaseOptionsAreValid(configuration, "Production"));
        StringAssert.Contains(ReadRuntimeDatabaseConnection(configuration), "Password=credential-probe");
        using var fixture = new DiagnoseWorkspace(configuration);
        foreach (var profile in new[] { "development", "production" })
            await AssertSecretDiagnosisAsync(fixture, profile, 0);
    }

    [TestMethod]
    [DataRow("fullnet-type")]
    [DataRow("cache-type")]
    [DataRow("connections-type")]
    [DataRow("enabled-type")]
    [DataRow("preset-type")]
    [DataRow("secret-type")]
    public async Task Repeated_json_sections_preserve_existing_nested_type_guards(string shape)
    {
        var configuration = RepeatedSectionConfiguration(shape);
        // 配置提供程序可装载这些标量与子键，但诊断必须保留已有字段类型约束。
        _ = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build();
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = File.ReadAllBytes(fixture.Settings);
        foreach (var profile in new[] { "development", "production" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(1, result, output.ToString());
            StringAssert.Contains(output.ToString(), "DIAG_APPSETTINGS_INVALID error");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
        }
    }

    private static string RepeatedSectionConfiguration(string shape)
    {
        var database = "\"Database\":{\"Provider\":\"MySql\",\"MySqlGuidStorageMode\":\"Binary16\",\"ConnectionName\":\"fullnet\"}";
        var connection = "\"ConnectionStrings\":{\"fullnet\":\"Server=example.invalid;Password=credential-probe\"}";
        var modules = "\"FullNet\":{\"Modules\":{\"Preset\":\"minimal\"}}";
        var fragment = shape switch
        {
            "root" => "\"Probe\":{\"One\":1},\"Probe\":{\"Two\":2}",
            "nested" => "\"Probe\":{\"Child\":{\"One\":1},\"Child\":{\"Two\":2}}",
            "modules" => "\"FullNet\":{\"Modules\":{\"Enabled\":[\"Identity\"]}}",
            "secrets" => "\"Cache\":{\"Probe\":1},\"Cache\":{\"RedisConnectionString\":\"credential-probe\"},\"Realtime\":{\"Probe\":1},\"Realtime\":{\"RedisBackplaneConnectionString\":\"credential-probe\"},\"FullNet\":{\"Cryptography\":{\"Sm2PrivateKeys\":{\"host-integration-signing\":\"credential-probe\"}}}",
            "empty-object" => "\"Probe\":{\"One\":1},\"Probe\":{}",
            "empty-array" => "\"Probe\":[1],\"Probe\":[]",
            "empty-then-object" => "\"Probe\":{},\"Probe\":{\"One\":1}",
            "secret-parent-empty" => "\"Cache\":{\"RedisConnectionString\":\"credential-probe\"},\"Cache\":{}",
            "connection-parent-empty" => "\"ConnectionStrings\":{}",
            "case-split" => "\"Probe\":{\"One\":1},\"PROBE\":{\"Two\":2},\"Probe\":{\"Three\":3}",
            "repeated-empty" => "\"Probe\":{},\"Probe\":{},\"Probe\":[]",
            "fullnet-type" => "\"FullNet\":\"credential-probe\"",
            "cache-type" => "\"Cache\":true,\"Cache\":{\"RedisConnectionString\":\"credential-probe\"}",
            "connections-type" => "\"ConnectionStrings\":42",
            "enabled-type" => "\"FullNet\":{\"Modules\":{\"Enabled\":42}}",
            "preset-type" => "\"FullNet\":{\"Modules\":{\"Preset\":true}},\"FullNet\":{\"Probe\":1}",
            "secret-type" => "\"Cache\":{\"RedisConnectionString\":true},\"Cache\":{\"Probe\":1}",
            _ => "\"Probe\":{\"One\":1}",
        };
        if (shape == "database") database = "\"Database\":{\"Provider\":\"MySql\"},\"Database\":{\"MySqlGuidStorageMode\":\"Binary16\"},\"Database\":{\"ConnectionName\":\"fullnet\"}";
        if (shape == "connections") fragment = "\"ConnectionStrings\":{\"other\":\"credential-probe\"}";
        if (shape == "nested-connections")
        {
            database = "\"Database\":{\"Provider\":\"MySql\",\"MySqlGuidStorageMode\":\"Binary16\",\"ConnectionName\":\"primary:target\"}";
            connection = "\"ConnectionStrings\":{\"primary\":{\"target\":\"Server=example.invalid;Password=credential-probe\"},\"primary\":{\"other\":\"credential-probe\"}}";
        }
        if (shape == "preset-type") modules = "\"FullNet\":{\"OtherProbe\":0}";
        return "{" + database + "," + connection + "," + modules + "," + fragment + "}";
    }

    [TestMethod]
    [DataRow("{}", "MISSING")]
    [DataRow("""{"FullNet":{"Modules":null}}""", "MISSING")]
    [DataRow("""{"FullNet:Modules":null}""", "MISSING")]
    [DataRow("""{"":{"FullNet":{"Modules":{"Preset":"minimal"}}}}""", "MISSING")]
    [DataRow("""{"FullNet":{"Modules":{}}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet:Modules":{}}""", "INCOMPLETE")]
    [DataRow("""{"fullnet":{"modules":{}}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Probe":1}}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet:Modules:Probe":1}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Enabled":[]}}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Enabled":null}}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet:Modules:Preset":" "}""", "INCOMPLETE")]
    [DataRow("""{"FullNet:Modules:Preset":null}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Preset":"minimal"}}}""", "OK")]
    [DataRow("""{"FullNet:Modules:Preset":"minimal"}""", "OK")]
    [DataRow("""{"FULLNET:MODULES:PRESET":"minimal"}""", "OK")]
    [DataRow("""{"fullnet":{"modules":{"preset":"minimal"}}}""", "OK")]
    [DataRow("""{"FullNet:Modules:Enabled:0":"Identity"}""", "OK")]
    [DataRow("""{"FULLNET:MODULES:ENABLED:0":"Identity"}""", "OK")]
    [DataRow("""{"FullNet:Modules:Enabled":{"first":"Identity"}}""", "OK")]
    [DataRow("""{"FullNet":{"Modules":{"Preset":"minimal"}},"FULLNET:MODULES:PRESET":{}}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Preset":"minimal"}},"FULLNET:MODULES:PRESET":[]}""", "INCOMPLETE")]
    [DataRow("""{"FullNet":{"Modules":{"Preset":"minimal"}},"FULLNET:MODULES:PRESET":{"Probe":1}}""", "OK")]
    [DataRow("""{"FullNet":{"Modules":{"Enabled":["Identity"]}},"FullNet":{"Modules":{"Enabled":[]}}}""", "OK")]
    [DataRow("""{"FullNet":{"Modules":{"Enabled":["Identity"]}},"FullNet":{"Modules":{"Enabled":null}}}""", "OK")]
    [DataRow("""{"FullNet":{"Modules":{"Enabled":["Identity"]}},"FullNet:Modules:Enabled":{}}""", "OK")]
    [DataRow("""{"fullnet":{"modules":{"enabled":["Identity"]}}}""", "OK")]
    public async Task Module_configuration_presence_matches_real_provider_paths(string moduleJson, string expected)
    {
        var baseline = JsonSerializer.Serialize(DiagnosticSecretSettings());
        var configuration = moduleJson == "{}" ? baseline : baseline[..^1] + "," + moduleJson[1..];
        var runtime = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration))).Build();
        // 空父节点不会删除已有子键；此处仅核对声明提示，不验证模块名称与依赖闭包。
        var configured = !string.IsNullOrWhiteSpace(runtime["FullNet:Modules:Preset"])
            || runtime.GetSection("FullNet:Modules:Enabled").GetChildren().Any();
        Assert.AreEqual(expected == "OK", configured);
        using var fixture = new DiagnoseWorkspace(configuration);
        var before = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        foreach (var profile in new[] { "development", "production" })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
            Assert.AreEqual(0, result, output.ToString());
            foreach (var code in new[] { "MISSING", "INCOMPLETE", "OK" })
                Assert.AreEqual(code == expected, output.ToString().Contains("DIAG_MODULES_" + code + " ", StringComparison.Ordinal), output.ToString());
            StringAssert.Contains(output.ToString(), "DIAG_MODULES_" + expected + (expected == "OK" ? " ok" : " warn"));
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(before.Keys.ToArray(), Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToArray());
            foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
    }

    private static readonly string[] DiagnosticSecretPaths =
    [
        "Cache:RedisConnectionString", "Realtime:RedisBackplaneConnectionString",
        "FullNet:Cryptography:Sm2PrivateKeys:host-integration-signing",
    ];

    private static Dictionary<string, object?> DiagnosticSecretSettings() => new()
    {
        ["Database"] = new { Provider = "MySql", MySqlGuidStorageMode = "Binary16", ConnectionString = "Server=example.invalid;Password=credential-probe" },
    };

    private static void AddNestedSecret(Dictionary<string, object?> settings, string path, object? value)
    {
        var segments = path.Split(':');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!settings.TryGetValue(segments[index], out var child))
                settings[segments[index]] = child = new Dictionary<string, object?>();
            settings = (Dictionary<string, object?>)child!;
        }
        settings[segments[^1]] = value;
    }

    private static async Task AssertSecretDiagnosisAsync(DiagnoseWorkspace fixture, string profile, int placeholders)
    {
        var before = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CodeGenerationCli.RunAsync(["diagnose", "--workspace", fixture.Root, "--profile", profile], output, error);
        Assert.AreEqual(profile == "production" && placeholders > 0 ? 1 : 0, result, output.ToString());
        StringAssert.Contains(output.ToString(), placeholders == 0 ? "DIAG_SECRETS_OK ok"
            : profile == "production" ? "DIAG_SECRETS_PLACEHOLDER error" : "DIAG_SECRETS_PLACEHOLDER warn");
        if (placeholders > 0) StringAssert.Contains(output.ToString(), $"有 {placeholders} 个秘密");
        Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
        var after = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).ToArray();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after);
        foreach (var path in before.Keys) CollectionAssert.AreEqual(before[path], File.ReadAllBytes(path));
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
