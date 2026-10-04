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
        var before = File.ReadAllBytes(fixture.Settings);
        try
        {
            Environment.SetEnvironmentVariable(environmentName, connection);
            var result = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "production"], output, error);

            Assert.AreEqual(0, result);
            StringAssert.Contains(output.ToString(), "DIAG_CONNECTION_CONFIGURED ok");
            Assert.IsFalse((output.ToString() + error).Contains("credential-probe", StringComparison.Ordinal));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Settings));
            Assert.AreEqual(1, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
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

    private sealed class DiagnoseWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fullnet-diagnose-{Guid.NewGuid():N}");
        public string Settings => Path.Combine(Root, "appsettings.json");
        private string? userSecretsDirectory;

        public DiagnoseWorkspace(string configuration = "{}")
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Settings, configuration, new UTF8Encoding(false));
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
