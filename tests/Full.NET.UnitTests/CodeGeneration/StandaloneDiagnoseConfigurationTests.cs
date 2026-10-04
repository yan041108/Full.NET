using System.Text;
using System.Text.Json;
using Full.NET.CodeGeneration.Cli;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class StandaloneDiagnoseConfigurationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Matching_configuration_is_readonly_and_legacy_application_does_not_require_migrator(bool migrator)
    {
        using var fixture = new StandaloneWorkspace(migrator, worker: false);
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_OK ok");
        StringAssert.Contains(result.Output, "DIAG_MODULE_CLOSURE_OK ok");
    }

    [TestMethod]
    [DataRow("appsettings.json", "provider")]
    [DataRow("appsettings.json", "preset")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "provider")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "preset")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "provider")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "preset")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "provider")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "preset")]
    public async Task Any_declared_base_configuration_drift_rejects_frozen_profile(string path, string field)
    {
        using var fixture = new StandaloneWorkspace();
        fixture.Write(path, Configuration(field == "provider" ? "sqlserver" : "mysql",
            field == "preset" ? "full" : "minimal"));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_MISMATCH error");
        Assert.IsFalse(result.Output.Contains("DIAG_APP_PROFILE_OK", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("appsettings.json", "{credential-probe")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "{credential-probe")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "{credential-probe")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "{credential-probe")]
    [DataRow("appsettings.json", "{\"FullNet\":\"credential-probe\"}")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "{\"FullNet\":\"credential-probe\"}")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "{\"FullNet\":\"credential-probe\"}")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "{\"FullNet\":\"credential-probe\"}")]
    public async Task Invalid_base_json_or_shape_returns_redacted_diagnostic(string path, string content)
    {
        using var fixture = new StandaloneWorkspace();
        fixture.Write(path, content);
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
        Assert.IsFalse(result.Output.Contains("DIAG_APP_PROFILE_OK", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("appsettings.json")]
    [DataRow("src/Demo.Host.Api/appsettings.json")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json")]
    [DataRow("src/Demo.Host.Worker/appsettings.json")]
    public async Task Missing_declared_configuration_is_not_hidden_by_api_or_root_fallback(string path)
    {
        using var fixture = new StandaloneWorkspace();
        File.Delete(fixture.PathFor(path));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
        Assert.IsFalse(result.Output.Contains("DIAG_APP_PROFILE_OK", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("appsettings.json")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json")]
    public async Task Configuration_directory_placeholder_returns_machine_diagnostic(string path)
    {
        using var fixture = new StandaloneWorkspace();
        File.Delete(fixture.PathFor(path));
        Directory.CreateDirectory(fixture.PathFor(path));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
    }

    [TestMethod]
    public async Task Declared_migrator_path_file_cannot_silently_turn_into_legacy_application()
    {
        using var fixture = new StandaloneWorkspace(migrator: false);
        fixture.Write("src/Demo.Host.Migrator", "credential-probe");
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
    }

    [TestMethod]
    public async Task Worker_declared_by_application_profile_cannot_disappear_silently()
    {
        using var fixture = new StandaloneWorkspace();
        Directory.Delete(fixture.PathFor("src/Demo.Host.Worker"), recursive: true);
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
    }

    [TestMethod]
    public async Task Worker_health_port_drift_rejects_frozen_profile()
    {
        using var fixture = new StandaloneWorkspace();
        fixture.Write("src/Demo.Host.Worker/appsettings.json", Configuration(workerPort: 5182));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_MISMATCH error");
    }

    [TestMethod]
    public async Task Missing_both_api_and_root_configurations_returns_error_instead_of_warning_only()
    {
        using var fixture = new StandaloneWorkspace();
        File.Delete(fixture.PathFor("appsettings.json"));
        File.Delete(fixture.PathFor("src/Demo.Host.Api/appsettings.json"));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
    }

    [TestMethod]
    public async Task Unreadable_api_configuration_preserves_redacted_machine_findings()
    {
        using var fixture = new StandaloneWorkspace();
        var before = fixture.ReadFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode;
        // 真实共享锁使文件可发现但不可读取，不能用删除文件替代IO错误路径。
        using (var held = new FileStream(fixture.PathFor("src/Demo.Host.Api/appsettings.json"),
            FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exitCode = await CodeGenerationCli.RunAsync(
                ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);
        }

        Assert.AreEqual(1, exitCode);
        var allOutput = output.ToString() + error;
        StringAssert.Contains(allOutput, "DIAG_APP_PROFILE_INVALID error");
        StringAssert.Contains(allOutput, "DIAG_APPSETTINGS_INVALID error");
        Assert.IsFalse(allOutput.Contains("credential-probe", StringComparison.Ordinal));
        Assert.IsFalse(allOutput.Contains("生成运行失败", StringComparison.Ordinal));
        var after = fixture.ReadFiles();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var path in before.Keys) CollectionAssert.AreEqual(before[path], after[path], path);
    }

    [TestMethod]
    [DataRow("appsettings.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("fullnet-app.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("framework-manifest.json", "DIAG_MODULE_CLOSURE_INVALID")]
    public async Task Duplicate_json_properties_return_redacted_machine_diagnostic(string path, string code)
    {
        using var fixture = new StandaloneWorkspace();
        var json = File.ReadAllText(fixture.PathFor(path));
        fixture.Write(path, json[..^1] + ",\"credential-probe\":1,\"credential-probe\":2}");
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, code + " error");
    }

    [TestMethod]
    [DataRow("appsettings.json", false)]
    [DataRow("appsettings.json", true)]
    [DataRow("src/Demo.Host.Api/appsettings.json", false)]
    [DataRow("src/Demo.Host.Api/appsettings.json", true)]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", false)]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", true)]
    [DataRow("src/Demo.Host.Worker/appsettings.json", false)]
    [DataRow("src/Demo.Host.Worker/appsettings.json", true)]
    public async Task Base_json_comments_and_trailing_commas_match_host_loader(string path, bool trailingComma)
    {
        using var fixture = new StandaloneWorkspace();
        var json = Configuration();
        var content = trailingComma ? json[..^1] + ",}" : "// credential-probe comment\n" + json;
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(content))).Build();
        Assert.AreEqual("mysql", configuration["Database:Provider"]);
        fixture.Write(path, content);
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_OK ok");
    }

    [TestMethod]
    [DataRow("appsettings.json", "case")]
    [DataRow("appsettings.json", "flat")]
    [DataRow("appsettings.json", "array")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "case")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "flat")]
    [DataRow("src/Demo.Host.Api/appsettings.json", "array")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "case")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "flat")]
    [DataRow("src/Demo.Host.Migrator/appsettings.json", "array")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "case")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "flat")]
    [DataRow("src/Demo.Host.Worker/appsettings.json", "array")]
    public async Task Base_json_duplicate_configuration_paths_reject_frozen_profile(string path, string shape)
    {
        using var fixture = new StandaloneWorkspace();
        var fragment = shape switch
        {
            "case" => "\"credential-probe\":1,\"CREDENTIAL-PROBE\":2",
            "flat" => "\"Probe\":{\"Value\":1},\"probe:value\":\"credential-probe\"",
            _ => "\"Probe\":[1],\"probe:0\":\"credential-probe\"",
        };
        var json = Configuration();
        var content = json[..^1] + "," + fragment + "}";
        Assert.ThrowsExactly<FormatException>(() => new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(content))).Build());
        fixture.Write(path, content);
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "DIAG_APP_PROFILE_INVALID error");
        Assert.IsFalse(result.Output.Contains("DIAG_APP_PROFILE_OK", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("fullnet-app.json", "DIAG_APP_PROFILE_INVALID")]
    [DataRow("framework-manifest.json", "DIAG_MODULE_CLOSURE_INVALID")]
    public async Task Base_json_tolerance_does_not_relax_frozen_manifest_json(string path, string code)
    {
        using var fixture = new StandaloneWorkspace();
        fixture.Write(path, "// credential-probe comment\n" + File.ReadAllText(fixture.PathFor(path)));
        var result = await DiagnoseAsync(fixture);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, code + " error");
    }

    private static async Task<(int ExitCode, string Output)> DiagnoseAsync(StandaloneWorkspace fixture)
    {
        var before = fixture.ReadFiles();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CodeGenerationCli.RunAsync(
            ["diagnose", "--workspace", fixture.Root, "--profile", "development"], output, error);
        var allOutput = output.ToString() + error;
        Assert.IsFalse(allOutput.Contains("credential-probe", StringComparison.Ordinal));
        var after = fixture.ReadFiles();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var path in before.Keys) CollectionAssert.AreEqual(before[path], after[path], path);
        return (exitCode, allOutput);
    }

    private static string Configuration(string provider = "mysql", string preset = "minimal", int workerPort = 5181) =>
        JsonSerializer.Serialize(new
        {
            Database = new { Provider = provider, ConnectionName = "app" },
            FullNet = new { Modules = new { Preset = preset } },
            Kestrel = new { Endpoints = new { Http = new { Url = $"http://localhost:{workerPort}" } } },
            ConnectionStrings = new { app = "Server=example.invalid;Password=credential-probe" },
        });

    private sealed class StandaloneWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "fullnet-standalone-diagnose-" + Guid.NewGuid().ToString("N"));
        public string PathFor(string path) => Path.Combine(Root, path);

        public StandaloneWorkspace(bool migrator = true, bool worker = true)
        {
            // 仅构造诊断读取所需布局；不编译项目，不启动宿主或连接数据库。
            Write("fullnet-app.json", worker
                ? """{"preset":"minimal","databaseProvider":"mysql","workerHttpPort":5181}"""
                : """{"preset":"minimal","databaseProvider":"mysql"}""");
            Write("framework-manifest.json", """{"presetModules":{"minimal":["Identity"]}}""");
            Write("appsettings.json", Configuration());
            Write("src/Demo.Host.Api/Demo.Host.Api.csproj", "<Project />");
            Write("src/Demo.Host.Api/appsettings.json", Configuration());
            Write("framework/fullnet/src/Composition/Full.NET.Composition/Full.NET.Composition.csproj",
                """<Project><ItemGroup><ProjectReference Include="../../Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj" /></ItemGroup></Project>""");
            Write("framework/fullnet/src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj", "<Project />");
            if (migrator)
            {
                Write("src/Demo.Host.Migrator/Demo.Host.Migrator.csproj", "<Project />");
                Write("src/Demo.Host.Migrator/appsettings.json", Configuration());
            }
            if (worker)
            {
                Write("src/Demo.Host.Worker/Demo.Host.Worker.csproj", "<Project />");
                Write("src/Demo.Host.Worker/appsettings.json", Configuration());
            }
        }

        public void Write(string path, string content)
        {
            var target = PathFor(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }

        public Dictionary<string, byte[]> ReadFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Root, path), File.ReadAllBytes, StringComparer.Ordinal);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
