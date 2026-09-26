using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
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

    private sealed class DiagnoseWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fullnet-diagnose-{Guid.NewGuid():N}");
        public string Settings => Path.Combine(Root, "appsettings.json");

        public DiagnoseWorkspace(string configuration = "{}")
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Settings, configuration, new UTF8Encoding(false));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
