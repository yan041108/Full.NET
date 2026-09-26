using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 只读环境诊断：检查 SDK、工作区结构、模块配置与秘密占位符，不输出凭据原文。
/// </summary>
internal static class DiagnoseCommand
{
    private static readonly string[] RequiredWorkspaceMarkers =
    [
        "src/Composition",
        "src/Hosts",
        "src/Modules",
    ];

    private static readonly string[] SecretPlaceholderPaths =
    [
        "Cache:RedisConnectionString",
        "Realtime:RedisBackplaneConnectionString",
        "FullNet:Cryptography:Sm2PrivateKeys:host-integration-signing",
    ];

    public static async Task<int> RunAsync(
        DiagnoseCliOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<DiagnoseFinding>();
        await CheckDotNetSdkAsync(options.WorkspacePath, findings, cancellationToken).ConfigureAwait(false);
        CheckWorkspaceStructure(options.WorkspacePath, findings);
        CheckAppsettings(options.WorkspacePath, options.Profile, findings);
        return await EmitAsync(findings, output, error).ConfigureAwait(false);
    }

    private static async Task CheckDotNetSdkAsync(
        string workspacePath,
        List<DiagnoseFinding> findings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                // SDK 解析必须遵循目标应用的 global.json，不能使用 CLI 所在仓库的 SDK。
                WorkingDirectory = Path.GetFullPath(workspacePath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_SDK_MISSING",
                    ".NET SDK 不可用。",
                    "安装 .NET 10 SDK 并确保 dotnet 在 PATH 中。"));
                return;
            }

            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            var version = (await process.StandardOutput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false)).Trim();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(version))
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_SDK_MISSING",
                    ".NET SDK 不可用。",
                    "安装 .NET 10 SDK 并确保 dotnet 在 PATH 中。"));
                return;
            }

            findings.Add(DiagnoseFinding.Ok(
                "DIAG_SDK_OK",
                $"检测到 .NET SDK {version}。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_SDK_MISSING",
                ".NET SDK 不可用。",
                "安装 .NET 10 SDK 并确保 dotnet 在 PATH 中。"));
        }
    }

    private static void CheckWorkspaceStructure(
        string workspacePath,
        List<DiagnoseFinding> findings)
    {
        if (!Directory.Exists(workspacePath))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_WORKSPACE_MISSING",
                "工作区目录不存在。",
                "使用 --workspace 指向 Full.NET 应用根目录。"));
            return;
        }

        if (File.Exists(Path.Combine(workspacePath, "fullnet-app.json")))
        {
            var host = FindStandaloneHost(workspacePath);
            if (host is not null
                && Directory.Exists(Path.Combine(workspacePath, "framework/fullnet/src/Composition"))
                && Directory.Exists(Path.Combine(workspacePath, "framework/fullnet/src/Modules")))
            {
                findings.Add(DiagnoseFinding.Ok(
                    "DIAG_WORKSPACE_OK",
                    "独立应用工作区结构完整。"));
            }
            else
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_WORKSPACE_INCOMPLETE",
                    "独立应用缺少宿主或框架源码目录。",
                    "检查 src/<name>.Host.Api 与 framework/fullnet/src/Composition、Modules。"));
            }

            return;
        }

        var missing = RequiredWorkspaceMarkers
            .Where(marker => !Directory.Exists(Path.Combine(workspacePath, marker)))
            .ToArray();
        var hasSolution = File.Exists(Path.Combine(workspacePath, "Full.NET.slnx"))
            || Directory.GetFiles(workspacePath, "*.sln").Length > 0;
        if (missing.Length == 0 && hasSolution)
        {
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_WORKSPACE_OK",
                "工作区结构符合 Full.NET 应用布局。"));
            return;
        }

        findings.Add(DiagnoseFinding.Warn(
            "DIAG_WORKSPACE_INCOMPLETE",
            "工作区缺少预期目录或解决方案文件。",
            "确认在应用根目录运行；模板应用应包含 src/Composition、src/Hosts 与 src/Modules。"));
    }

    private static void CheckAppsettings(
        string workspacePath,
        string profile,
        List<DiagnoseFinding> findings)
    {
        var standaloneHost = File.Exists(Path.Combine(workspacePath, "fullnet-app.json"))
            ? FindStandaloneHost(workspacePath)
            : null;
        var candidates = new[]
        {
            standaloneHost is null ? null : Path.Combine(standaloneHost, "appsettings.json"),
            Path.Combine(workspacePath, "src/Hosts/Full.NET.Host.Api/appsettings.json"),
            Path.Combine(workspacePath, "src/App.Host.Api/appsettings.json"),
            Path.Combine(workspacePath, "appsettings.json"),
        };
        var appsettingsPath = candidates.FirstOrDefault(path => path is not null && File.Exists(path));
        if (appsettingsPath is null)
        {
            findings.Add(DiagnoseFinding.Warn(
                "DIAG_APPSETTINGS_MISSING",
                "未找到 appsettings.json。",
                "在 Host.Api 或应用根目录创建 appsettings.json。"));
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(appsettingsPath));
        }
        catch (JsonException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APPSETTINGS_INVALID",
                "appsettings.json 不是有效 JSON。",
                "修复 JSON 语法后再运行 diagnose。"));
            return;
        }

        if (root is null)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APPSETTINGS_INVALID",
                "appsettings.json 为空。",
                "填充 Database 与 FullNet:Modules 配置。"));
            return;
        }

        try
        {
            CheckModulesSection(root, findings);
            if (standaloneHost is not null)
            {
                CheckStandaloneAppProfile(workspacePath, root, findings);
                CheckStandaloneModuleClosure(workspacePath, findings);
            }
            CheckConnectionPlaceholder(root, appsettingsPath, workspacePath, profile, findings);
            CheckSecretPlaceholders(root, profile, findings);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            // 配置字段类型错误属于诊断结果；不要把含秘密的值或异常文本带入通用 CLI 输出。
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APPSETTINGS_INVALID",
                "appsettings.json 的配置结构或字段类型无效。",
                "检查 FullNet:Modules、Database、ConnectionStrings 与秘密配置的对象、数组和字符串类型。"));
        }
    }

    private static string? FindStandaloneHost(string workspacePath)
    {
        var sourcePath = Path.Combine(workspacePath, "src");
        if (!Directory.Exists(sourcePath))
        {
            return null;
        }

        var hosts = Directory.GetDirectories(sourcePath, "*.Host.Api", SearchOption.TopDirectoryOnly)
            .Where(path => File.Exists(Path.Combine(path, Path.GetFileName(path) + ".csproj")))
            .Take(2)
            .ToArray();
        return hosts.Length == 1 ? hosts[0] : null;
    }

    private static void CheckStandaloneAppProfile(
        string workspacePath,
        JsonNode runtime,
        List<DiagnoseFinding> findings)
    {
        try
        {
            var app = JsonNode.Parse(File.ReadAllText(Path.Combine(workspacePath, "fullnet-app.json")));
            var preset = app?["preset"]?.GetValue<string>();
            var provider = app?["databaseProvider"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(preset) || string.IsNullOrWhiteSpace(provider))
            {
                throw new JsonException("Missing application profile fields.");
            }

            var runtimePreset = runtime["FullNet"]?["Modules"]?["Preset"]?.GetValue<string>();
            var runtimeProvider = runtime["Database"]?["Provider"]?.GetValue<string>();
            if (!string.Equals(preset, runtimePreset, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(provider, runtimeProvider, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_APP_PROFILE_MISMATCH",
                    "独立应用清单与 API 宿主的模块预设或数据库 Provider 不一致。",
                    "核对 fullnet-app.json 与 src/<name>.Host.Api/appsettings.json；不要直接修改冻结的应用清单。"));
                return;
            }

            findings.Add(DiagnoseFinding.Ok(
                "DIAG_APP_PROFILE_OK",
                "独立应用清单与 API 宿主配置一致。"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APP_PROFILE_INVALID",
                "独立应用清单格式无效。",
                "检查 fullnet-app.json 中的 preset 与 databaseProvider。"));
        }
    }

    private static void CheckStandaloneModuleClosure(
        string workspacePath,
        List<DiagnoseFinding> findings)
    {
        try
        {
            var app = JsonNode.Parse(File.ReadAllText(Path.Combine(workspacePath, "fullnet-app.json")));
            var preset = app?["preset"]?.GetValue<string>();
            var manifestPath = Path.Combine(workspacePath, "framework-manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
            var selected = manifest?["presetModules"]?[preset ?? string.Empty]?.AsArray();
            if (selected is null || selected.Count == 0)
            {
                throw new JsonException("Selected preset has no module closure.");
            }

            var compositionRoot = Path.Combine(workspacePath,
                "framework/fullnet/src/Composition/Full.NET.Composition");
            var compositionProject = Path.Combine(compositionRoot, "Full.NET.Composition.csproj");
            var references = XDocument.Load(compositionProject).Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFullPath(Path.Combine(
                    compositionRoot, include!.Replace('\\', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in selected)
            {
                var module = entry?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(module)
                    || !char.IsLetter(module[0])
                    || !module.All(char.IsLetterOrDigit))
                {
                    throw new JsonException("Invalid module name in preset closure.");
                }

                var project = Path.GetFullPath(Path.Combine(workspacePath,
                    "framework/fullnet/src/Modules", $"Full.NET.Modules.{module}",
                    $"Full.NET.Modules.{module}.csproj"));
                if (!File.Exists(project) || !references.Contains(project))
                {
                    findings.Add(DiagnoseFinding.Error(
                        "DIAG_MODULE_DEPENDENCY_MISSING",
                        $"预设模块 {module} 缺少项目或 Composition 引用。",
                        "恢复受管框架源码，或重新用已验证的模板包创建应用。"));
                }
            }

            if (!findings.Any(f => f.Code == "DIAG_MODULE_DEPENDENCY_MISSING"))
            {
                findings.Add(DiagnoseFinding.Ok(
                    "DIAG_MODULE_CLOSURE_OK",
                    "所选预设模块的项目与 Composition 引用齐全。"));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or IOException or System.Xml.XmlException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_MODULE_CLOSURE_INVALID",
                "无法读取独立应用的预设模块闭包。",
                "检查 framework-manifest.json 与 framework/fullnet/src/Composition 项目文件。"));
        }
    }

    private static void CheckModulesSection(JsonNode root, List<DiagnoseFinding> findings)
    {
        var modules = root["FullNet"]?["Modules"];
        if (modules is null)
        {
            findings.Add(DiagnoseFinding.Warn(
                "DIAG_MODULES_MISSING",
                "未配置 FullNet:Modules。",
                "添加 FullNet:Modules:Preset（如 minimal、platform、full）。"));
            return;
        }

        var preset = modules["Preset"]?.GetValue<string>();
        var enabled = modules["Enabled"]?.AsArray();
        if (!string.IsNullOrWhiteSpace(preset) || enabled is { Count: > 0 })
        {
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_MODULES_OK",
                "已配置 FullNet:Modules 模块预设或启用列表。"));
            return;
        }

        findings.Add(DiagnoseFinding.Warn(
            "DIAG_MODULES_INCOMPLETE",
            "FullNet:Modules 存在但未设置 Preset 或 Enabled。",
            "设置 FullNet:Modules:Preset=minimal 或显式 Enabled 数组。"));
    }

    private static void CheckConnectionPlaceholder(
        JsonNode root,
        string appsettingsPath,
        string workspacePath,
        string profile,
        List<DiagnoseFinding> findings)
    {
        var connectionName = root["Database"]?["ConnectionName"]?.GetValue<string>() ?? "fullnet";
        var connectionStrings = root["ConnectionStrings"]?.AsObject();
        var hasInline = connectionStrings?[connectionName]?.GetValue<string>() is { Length: > 0 } inline
            && !string.IsNullOrWhiteSpace(inline) && !IsPlaceholder(inline);
        var envName = $"ConnectionStrings__{connectionName}";
        var environmentConnection = Environment.GetEnvironmentVariable(envName);
        var hasEnv = !string.IsNullOrWhiteSpace(environmentConnection) && !IsPlaceholder(environmentConnection);
        var userSecretsId = TryReadUserSecretsId(appsettingsPath, workspacePath);
        var hasUserSecrets = userSecretsId is not null
            && File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets",
                userSecretsId,
                "secrets.json"));

        if (hasInline || hasEnv || hasUserSecrets)
        {
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_CONNECTION_CONFIGURED",
                $"数据库连接名 {connectionName} 已通过配置或环境提供。"));
            return;
        }

        if (string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_CONNECTION_MISSING",
                $"生产配置缺少 ConnectionStrings:{connectionName}。",
                $"通过密钥管理或环境变量 {envName} 注入连接字符串；不要在仓库中提交凭据。"));
            return;
        }

        findings.Add(DiagnoseFinding.Warn(
            "DIAG_CONNECTION_PLACEHOLDER",
            $"开发环境尚未配置 ConnectionStrings:{connectionName}。",
            $"使用 dotnet user-secrets set \"ConnectionStrings:{connectionName}\" \"<your-connection>\" 或设置环境变量 {envName}。"));
    }

    private static void CheckSecretPlaceholders(
        JsonNode root,
        string profile,
        List<DiagnoseFinding> findings)
    {
        var placeholders = new List<string>();
        foreach (var path in SecretPlaceholderPaths)
        {
            if (IsEmptyPlaceholder(root, path))
            {
                placeholders.Add(path);
            }
        }

        if (placeholders.Count == 0)
        {
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_SECRETS_OK",
                "常见秘密占位符已填写或非空。"));
            return;
        }

        var hint = "通过 user-secrets 或部署密钥注入；诊断不会输出秘密值。";
        if (string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_SECRETS_PLACEHOLDER",
                $"生产环境仍有 {placeholders.Count} 个秘密占位符。",
                hint));
            return;
        }

        findings.Add(DiagnoseFinding.Warn(
            "DIAG_SECRETS_PLACEHOLDER",
            $"开发环境有 {placeholders.Count} 个秘密仍为空占位符。",
            hint));
    }

    private static bool IsEmptyPlaceholder(JsonNode root, string colonPath)
    {
        JsonNode? current = root;
        foreach (var segment in colonPath.Split(':'))
        {
            current = current?[segment];
            if (current is null)
            {
                return false;
            }
        }

        // 当前三个秘密配置的运行时契约均为字符串；错误类型不能被视为已配置。
        if (current is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            throw new InvalidOperationException("Secret configuration must be a string.");
        }

        return string.IsNullOrWhiteSpace(text) || IsPlaceholder(text);
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("<your-", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadUserSecretsId(string appsettingsPath, string workspacePath)
    {
        var projectCandidates = new[]
        {
            Path.Combine(Path.GetDirectoryName(appsettingsPath)!, "..", "..", "Full.NET.Host.Api.csproj"),
            Path.Combine(workspacePath, "src/App.Host.Api/App.Host.Api.csproj"),
            Path.Combine(workspacePath, "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj"),
        };
        foreach (var candidate in projectCandidates)
        {
            var path = Path.GetFullPath(candidate);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path))
            {
                const string marker = "<UserSecretsId>";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                var end = line.IndexOf("</UserSecretsId>", StringComparison.Ordinal);
                if (end > start)
                {
                    return line[(start + marker.Length)..end].Trim();
                }
            }
        }

        return null;
    }

    private static async Task<int> EmitAsync(
        List<DiagnoseFinding> findings,
        TextWriter output,
        TextWriter error)
    {
        foreach (var finding in findings.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            var line = $"{finding.Code} {finding.Severity} {finding.Message}";
            if (!string.IsNullOrWhiteSpace(finding.Hint))
            {
                line += $" hint={finding.Hint}";
            }

            await output.WriteLineAsync(line).ConfigureAwait(false);
        }

        var hasError = findings.Any(f => f.Severity == "error");
        var hasWarn = findings.Any(f => f.Severity == "warn");
        if (hasError)
        {
            await error.WriteLineAsync("DIAG_SUMMARY error").ConfigureAwait(false);
            return 1;
        }

        if (hasWarn)
        {
            await error.WriteLineAsync("DIAG_SUMMARY warn").ConfigureAwait(false);
            return 0;
        }

        await output.WriteLineAsync("DIAG_SUMMARY ok").ConfigureAwait(false);
        return 0;
    }

    private sealed record DiagnoseFinding(string Code, string Severity, string Message, string? Hint)
    {
        public static DiagnoseFinding Ok(string code, string message) =>
            new(code, "ok", message, null);

        public static DiagnoseFinding Warn(string code, string message, string hint) =>
            new(code, "warn", message, hint);

        public static DiagnoseFinding Error(string code, string message, string hint) =>
            new(code, "error", message, hint);
    }
}

internal sealed record DiagnoseCliOptions(string WorkspacePath, string Profile);
