using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 只读环境诊断：检查 SDK、工作区结构、模块配置与秘密占位符，不输出凭据原文。
/// </summary>
internal static partial class DiagnoseCommand
{
    private static readonly TimeSpan SdkProbeTimeout = TimeSpan.FromSeconds(30);
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

    private static readonly (string Prefix, string? ProviderName)[] ConnectionEnvironmentPrefixes =
    [
        ("MYSQLCONNSTR_", "MySql.Data.MySqlClient"),
        ("SQLCONNSTR_", "System.Data.SqlClient"),
        ("SQLAZURECONNSTR_", "System.Data.SqlClient"),
        ("CUSTOMCONNSTR_", null),
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

            findings.Add(await DiagnoseSdkProbeAsync(process, cancellationToken).ConfigureAwait(false));
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

    internal static async Task<DiagnoseFinding> DiagnoseSdkProbeAsync(
        Process process,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        try
        {
            var (exitCode, version) = await ReadSdkProbeAsync(process, cancellationToken, timeout).ConfigureAwait(false);
            return exitCode != 0 || string.IsNullOrWhiteSpace(version)
                ? DiagnoseFinding.Error("DIAG_SDK_MISSING", ".NET SDK 不可用。",
                    "安装 .NET 10 SDK 并确保 dotnet 在 PATH 中。")
                : DiagnoseSdkVersion(version);
        }
        catch (TimeoutException)
        {
            return DiagnoseFinding.Error("code_generation.sdk.probe_timeout", ".NET SDK 探测未在等待上限内完成。",
                "在目标工作区运行 dotnet --version，排查 SDK 启动卡住的问题；诊断不会输出原始进程内容。");
        }
    }

    internal static async Task<(int ExitCode, string Version)> ReadSdkProbeAsync(
        Process process,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var probe = ReadSdkProbeOutputAsync(process, probeCancellation.Token);
        try
        {
            // 上限覆盖进程退出及两个输出管道；部分输出不能延长探测等待。
            return await probe.WaitAsync(timeout ?? SdkProbeTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException
            || exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            // 父进程可能已退出但子进程仍持有输出管道；收尾读取也须结束等待。
            probeCancellation.Cancel();
            try
            {
                // Process.Dispose 不会停止子进程；取消或超时须先回收本次探测拥有的进程树。
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
                // 进程可能在终止前自行退出，此时继续保留已经确定的取消或超时结果。
            }

            try
            {
                await probe.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 关闭管道时的后续读取失败已被观察，不能替换既有结果或回显进程内容。
            }

            // 清理期间取消也优先于超时，异常继续携带调用方的原始令牌。
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static async Task<(int ExitCode, string Version)> ReadSdkProbeOutputAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(standardOutput, standardError, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
        return (process.ExitCode, (await standardOutput.ConfigureAwait(false)).Trim());
    }

    internal static DiagnoseFinding DiagnoseSdkVersion(string version)
    {
        var match = SdkVersionPattern().Match(version);
        // 当前源码目标 net10.0，分发包使用 10.0.100 + latestFeature；不把其他基线自动认证为兼容。
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed)
            || parsed.Major != 10 || parsed.Minor != 0 || parsed.Build < 100)
        {
            return DiagnoseFinding.Error(
                "DIAG_SDK_INCOMPATIBLE",
                "目标工作区选择的 SDK 不符合当前 .NET 10.0 SDK 基线，或返回的版本格式无效。",
                "安装 .NET 10 SDK，核对目标工作区及父目录的 global.json，再运行 dotnet --version；当前基线为 10.0.100 或更高的 10.0 SDK 功能带。");
        }

        // 仅回显已解析的数字版本；即使版本后缀被错误填入凭据，也不能带入诊断输出。
        var preview = match.Groups["prerelease"].Success ? "（预览版）" : string.Empty;
        return DiagnoseFinding.Ok("DIAG_SDK_OK", $"检测到 .NET SDK {parsed}{preview}。");
    }

    [GeneratedRegex(@"\A(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SdkVersionPattern();

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
        if (standaloneHost is not null)
        {
            // 冻结档案检查独立于连接配置回退，缺少API与根配置也必须明确失败。
            CheckStandaloneAppProfile(workspacePath, standaloneHost, findings);
            CheckStandaloneModuleClosure(workspacePath, findings);
        }
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

        JsonDocument settings;
        try
        {
            settings = ReadConfigurationDocument(appsettingsPath);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APPSETTINGS_INVALID",
                "appsettings.json 不可读取或不是有效 JSON。",
                "检查文件读取权限并修复 JSON 格式后再运行 diagnose。"));
            return;
        }

        // 保留原始对象声明及顺序；JsonNode 的唯一属性字典无法表示合法的分段对象配置。
        using var baseSettings = settings;
        var root = baseSettings.RootElement;
        try
        {
            using var profileSettings = ReadProfileSettings(appsettingsPath, profile);
            if (string.Equals(profile, "development", StringComparison.OrdinalIgnoreCase)
                && TryReadUserSecretsId(workspacePath) is { } userSecretsId
                && !IsValidUserSecretsFile(userSecretsId))
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_USER_SECRETS_INVALID",
                    "API 项目的 User Secrets 文件不可读取或配置结构无效。",
                    "修复秘密文件的 JSON 语法与重复配置键；诊断不会输出秘密值。"));
            }
            CheckModulesSection(root, findings);
            var moduleSelection = CheckModulePreset(root, profileSettings, workspacePath, profile, findings);
            if (standaloneHost is not null && moduleSelection is not null)
            {
                var selected = CheckStandaloneModuleAvailability(workspacePath, moduleSelection, findings);
                if (selected is not null) CheckStandaloneModuleDependencies(workspacePath, selected, findings);
            }
            CheckDatabaseProvider(root, profileSettings, workspacePath, profile, findings);
            CheckDatabaseOptions(root, profileSettings, workspacePath, profile, findings);
            CheckConnectionPlaceholder(root, profileSettings, workspacePath, profile, findings);
            CheckDatabaseCapacity(root, profileSettings, workspacePath, profile, findings);
            CheckSecretPlaceholders(root, profileSettings, workspacePath, profile, findings);
            CheckIdentityNumericOptions(root, profileSettings, workspacePath, profile, findings);
            CheckIdentityProtocolOptions(root, profileSettings, workspacePath, profile, findings);
            CheckIdentitySecurityOptions(root, profileSettings, workspacePath, profile, findings);
            CheckIdentityCorsCredentials(root, profileSettings, workspacePath, profile, findings);
            CheckIdentitySigning(root, profileSettings, workspacePath, profile, findings);
            CheckOidcSigning(root, profileSettings, workspacePath, profile, findings);
            CheckOidcIssuerAndEncryption(root, profileSettings, workspacePath, profile, findings);
            CheckOidcClients(root, profileSettings, workspacePath, profile, findings);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException
            or ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 字段类型或重复属性错误属于诊断结果，不能回显含秘密的属性名、值或异常文本。
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APPSETTINGS_INVALID",
                "基础或所选环境 appsettings 配置不可读取、结构或字段类型无效。",
                "检查对应 JSON 文件的读取权限、语法、重复键及配置字段类型；诊断不会输出秘密值。"));
        }
    }

    private static JsonDocument? ReadProfileSettings(string appsettingsPath, string profile)
    {
        // CLI 的小写 profile 映射到默认宿主的规范环境名；Linux 文件名区分大小写。
        var environmentName = string.Equals(profile, "development", StringComparison.OrdinalIgnoreCase)
            ? "Development" : "Production";
        var path = Path.Combine(Path.GetDirectoryName(appsettingsPath)!, $"appsettings.{environmentName}.json");
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        return ReadConfigurationDocument(path);
    }

    private static JsonDocument ReadConfigurationDocument(string path)
    {
        // 宿主配置允许注释和尾逗号，但必须在覆盖取值前拒绝不区分大小写的重复展平路径。
        var document = JsonDocument.Parse(File.ReadAllText(path), ConfigurationJsonOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !HasUniqueConfigurationPaths(document.RootElement, null,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
        {
            document.Dispose();
            throw new JsonException("Invalid application configuration.");
        }
        return document;
    }

    private static bool TryReadConfigurationOverride(
        JsonDocument? profileSettings, string workspacePath, string profile, string path, out string? text,
        bool requireValidUserSecrets = false, bool includeScalarValues = false)
    {
        text = GetEnvironmentConfigurationValue(path);
        if (text is not null)
        {
            return true;
        }

        // 默认宿主按环境变量、Development User Secrets、环境 JSON、基础 JSON 的顺序取值。
        if (string.Equals(profile, "development", StringComparison.OrdinalIgnoreCase)
            && TryReadUserSecretsId(workspacePath) is { } id
            && (!requireValidUserSecrets || IsValidUserSecretsFile(id))
            && TryReadUserSecret(id, path, out text, includeScalarValues))
        {
            return true;
        }
        if (profileSettings is null)
        {
            return false;
        }

        var found = false;
        VisitConfigurationValue(profileSettings.RootElement, null, path, ref found, ref text, includeScalarValues);
        return found;
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
        string standaloneHost,
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
            var workerPort = app is JsonObject workerProfile && workerProfile.ContainsKey("workerHttpPort")
                ? workerProfile["workerHttpPort"]?.GetValue<int>()
                    ?? throw new JsonException("Invalid Worker health port.")
                : (int?)null;
            if (workerPort is < 1 or > 65535)
            {
                throw new JsonException("Invalid Worker health port.");
            }

            var configurationPaths = new List<string>
            {
                Path.Combine(workspacePath, "appsettings.json"),
                Path.Combine(standaloneHost, "appsettings.json"),
            };
            var apiName = Path.GetFileName(standaloneHost);
            var migratorRoot = Path.Combine(Path.GetDirectoryName(standaloneHost)!,
                apiName[..^".Host.Api".Length] + ".Host.Migrator");
            // 旧应用可以没有Migrator，但目录或文件已占用该位置时不能静默忽略。
            if (Directory.Exists(migratorRoot) || File.Exists(migratorRoot))
            {
                configurationPaths.Add(Path.Combine(migratorRoot, "appsettings.json"));
            }
            var workerRoot = Path.Combine(Path.GetDirectoryName(standaloneHost)!,
                apiName[..^".Host.Api".Length] + ".Host.Worker");
            // 新应用档案声明 Worker 后必须持续校验；旧应用只在确有该宿主时检查。
            if (app is JsonObject appObject && appObject.ContainsKey("workerHttpPort")
                || Directory.Exists(workerRoot) || File.Exists(workerRoot))
            {
                configurationPaths.Add(Path.Combine(workerRoot, "appsettings.json"));
            }

            foreach (var path in configurationPaths)
            {
                using var settings = ReadConfigurationDocument(path);
                var runtime = settings.RootElement;
                var runtimePreset = ReadStandaloneConfigurationValue(runtime, "FullNet:Modules:Preset");
                var runtimeProvider = ReadStandaloneConfigurationValue(runtime, "Database:Provider");
                if (!string.Equals(preset, runtimePreset, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(provider, runtimeProvider, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(DiagnoseFinding.Error(
                        "DIAG_APP_PROFILE_MISMATCH",
                        "独立应用清单与根配置、API、Worker 或 Migrator 的模块预设或数据库 Provider 不一致。",
                        "核对根与同名宿主的基础 appsettings.json；不要直接修改冻结的应用清单。"));
                    return;
                }
                if (workerPort is int expectedPort
                    && path.EndsWith(".Host.Worker" + Path.DirectorySeparatorChar + "appsettings.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var endpoint = ReadStandaloneConfigurationValue(runtime, "Kestrel:Endpoints:Http:Url");
                    if (!MatchesWorkerHealthEndpoint(endpoint, expectedPort))
                    {
                        findings.Add(DiagnoseFinding.Error(
                            "DIAG_APP_PROFILE_MISMATCH",
                            "Worker 健康监听地址无效或端口与独立应用清单不一致。",
                            "核对同名 Worker 的基础 appsettings.json 与 fullnet-app.json；监听地址须使用 HTTP/HTTPS、根路径及声明端口，支持 * 与 + 通配主机。"));
                        return;
                    }
                }
            }

            findings.Add(DiagnoseFinding.Ok(
                "DIAG_APP_PROFILE_OK",
                "独立应用清单与根、API 及已声明 Worker/Migrator 的基础配置一致。"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_APP_PROFILE_INVALID",
                "独立应用清单或基础配置缺失、不可读取或格式无效。",
                "检查 fullnet-app.json 以及根、API 和已声明 Worker/Migrator 的基础 appsettings.json。"));
        }
    }

    private static string? ReadStandaloneConfigurationValue(JsonElement root, string path)
    {
        // 保留原有嵌套字段的字符串类型检查，再以宿主展平语义读取大小写别名和空集合覆盖。
        foreach (var current in ReadNestedConfigurationValues(root, path)) _ = current.GetString();
        _ = TryReadBaseConfigurationValue(root, path, out var value);
        return value;
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
                // Linux 的大小写不同路径可指向不同项目，不能用 Windows 的比较规则认证引用。
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
            or FormatException or ArgumentException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_MODULE_CLOSURE_INVALID",
                "无法读取独立应用的预设模块闭包。",
                "检查 framework-manifest.json 与 framework/fullnet/src/Composition 项目文件。"));
        }
    }

    private static void CheckModulesSection(JsonElement root, List<DiagnoseFinding> findings)
    {
        var modules = ReadNestedConfigurationValues(root, "FullNet:Modules")
            .Where(value => value.ValueKind != JsonValueKind.Null).ToArray();
        // 分段对象须逐个保留类型约束，不能因另一个有效片段而掩盖错误结构。
        foreach (var section in modules) _ = section.EnumerateObject();
        foreach (var value in ReadNestedConfigurationValues(root, "FullNet:Modules:Preset")) _ = value.GetString();
        foreach (var value in ReadNestedConfigurationValues(root, "FullNet:Modules:Enabled"))
            if (value.ValueKind != JsonValueKind.Null) _ = value.GetArrayLength();

        var leaves = EnumerateConfigurationLeaves(root, null).ToArray();
        var moduleRoot = leaves.LastOrDefault(leaf => leaf.Path.Equals("FullNet:Modules", StringComparison.OrdinalIgnoreCase));
        var declared = leaves.Any(leaf => leaf.Path.StartsWith("FullNet:Modules:", StringComparison.OrdinalIgnoreCase))
            || moduleRoot.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        if (!declared)
        {
            findings.Add(DiagnoseFinding.Warn(
                "DIAG_MODULES_MISSING",
                "未配置 FullNet:Modules。",
                "添加 FullNet:Modules:Preset（如 minimal、platform、full）。"));
            return;
        }

        _ = TryReadBaseConfigurationValue(root, "FullNet:Modules:Preset", out var preset);
        // 声明提示只看生效标量与子键；空父节点不会清除配置提供程序已有的数组子键。
        var hasEnabledChildren = leaves.Any(leaf => leaf.Path.StartsWith("FullNet:Modules:Enabled:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preset) || hasEnabledChildren)
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

    private static void CheckDatabaseProvider(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile, "Database:Provider", out var value);

        // 缺省或 null 保留运行时 SqlServer 默认值；显式空字符串仍会使枚举绑定失败。
        if (value is null || (Enum.TryParse<DiagnosticDatabaseProvider>(value, true, out var provider)
            && Enum.IsDefined(provider)))
        {
            return;
        }
        findings.Add(DiagnoseFinding.Error(
            "DIAG_DATABASE_PROVIDER_INVALID",
            "Database:Provider 不能绑定为受支持的数据库提供程序。",
            "核对最终生效的 Database:Provider，使用 SqlServer 或 MySql；诊断不会输出配置值。"));
    }

    // 工具不引入运行时数据依赖；枚举值与真实 Dapper Options 的一致性由回归测试约束。
    private enum DiagnosticDatabaseProvider
    {
        SqlServer = 0,
        MySql = 1,
    }

    private static bool TryReadDatabaseValue(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        string path, out string? value)
    {
        if (TryReadConfigurationOverride(profileSettings, workspacePath, profile, path, out value,
                requireValidUserSecrets: true, includeScalarValues: true))
        {
            return true;
        }
        return TryReadBaseConfigurationValue(root, path, out value, includeScalarValues: true);
    }

    private static bool TryReadBaseConfigurationValue(
        JsonElement root, string path, out string? value, bool includeScalarValues = false)
    {
        var found = false;
        value = null;
        VisitConfigurationValue(root, null, path, ref found, ref value, includeScalarValues);
        return found;
    }

    // 展平叶键保留冒号属性名、大小写别名和分段对象；空集合仍是父路径的显式声明。
    private static IEnumerable<(string Path, JsonElement Value)> EnumerateConfigurationLeaves(JsonElement element, string? path)
    {
        if (element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any())
        {
            foreach (var property in element.EnumerateObject())
                foreach (var leaf in EnumerateConfigurationLeaves(property.Value, path is null ? property.Name : path + ":" + property.Name))
                    yield return leaf;
        }
        else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                foreach (var leaf in EnumerateConfigurationLeaves(item, path + ":" + index++))
                    yield return leaf;
        }
        else if (path is not null)
        {
            yield return (path, element);
        }
    }

    // 仅对原有精确嵌套路径执行字段类型检查；实际配置取值仍走展平、大小写不敏感的读取。
    private static IEnumerable<JsonElement> ReadNestedConfigurationValues(JsonElement root, string path) =>
        ReadNestedConfigurationValues(root, path.Split(':'), 0);

    private static IEnumerable<JsonElement> ReadNestedConfigurationValues(
        JsonElement element, string[] segments, int index)
    {
        if (index == segments.Length)
        {
            yield return element;
            yield break;
        }
        if (element.ValueKind == JsonValueKind.Null) yield break;
        // 遍历每个同名对象而非只取最后一个；非对象中间节点继续按原有结构约束失败。
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(segments[index])) continue;
            foreach (var value in ReadNestedConfigurationValues(property.Value, segments, index + 1))
                yield return value;
        }
    }

    private static void CheckDatabaseOptions(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        var hasTimeout = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "Database:CommandTimeoutSeconds", out var timeout);
        // 缺省保留 30 秒；显式 null 绑定为 0，不能回退到低优先级的正值。
        if (hasTimeout && !IsPositiveDatabaseTimeout(timeout))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_DATABASE_TIMEOUT_INVALID",
                "Database:CommandTimeoutSeconds 不能绑定为正整数。",
                "将最终生效的 Database:CommandTimeoutSeconds 设置为正整数秒数；诊断不会输出配置值。"));
        }

        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "Database:MySqlGuidStorageMode", out var storage);
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "Database:Provider", out var providerValue);
        var provider = DiagnosticDatabaseProvider.SqlServer;
        if (providerValue is not null) _ = Enum.TryParse(providerValue, true, out provider);
        var mode = DiagnosticGuidStorageMode.LegacyChar36;
        var validMode = storage is null || (Enum.TryParse(storage, true, out mode) && Enum.IsDefined(mode));
        var production = string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase);
        // 复用宿主既有准入：Production 两库均需显式模式，MySQL 还必须使用 Binary16。
        if (!validMode || (production && (storage is null
            || (provider == DiagnosticDatabaseProvider.MySql && mode != DiagnosticGuidStorageMode.Binary16))))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_DATABASE_GUID_STORAGE_INVALID",
                "Database:MySqlGuidStorageMode 不符合所选环境的启动要求。",
                "使用 LegacyChar36 或 Binary16；Production 必须显式配置，MySQL 必须使用 Binary16。诊断不会输出配置值。"));
        }
    }

    private static bool IsPositiveDatabaseTimeout(string? value) =>
        value is not null && TryParseConfigurationInt32(value, out var timeout) && timeout > 0;

    private static bool TryParseConfigurationInt32(string value, out int number)
    {
        number = 0;
        value = value.Trim();
        try
        {
            // Int32 配置转换支持十进制及这三种十六进制前缀，不能比运行时更窄。
            number = value.StartsWith('#') ? Convert.ToInt32(value[1..], 16)
                : value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("&h", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(value[2..], 16)
                    : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    // 与运行时存储枚举的名称和值保持一致，真实 Options 对照测试约束此工具边界。
    private enum DiagnosticGuidStorageMode
    {
        LegacyChar36 = 0,
        Binary16 = 1,
    }

    private static void CheckConnectionPlaceholder(
        JsonElement root,
        JsonDocument? profileSettings,
        string workspacePath,
        string profile,
        List<DiagnoseFinding> findings)
    {
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
            "Database:ConnectionString", out var effectiveConnection);
        // 与 Dapper PostConfigure 一致：非空直配优先，即使是占位符也不能用命名连接掩盖。
        if (string.IsNullOrWhiteSpace(effectiveConnection))
        {
            var hasConnectionName = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
                "Database:ConnectionName", out var connectionName);
            // 只有未出现标量键才保留宿主默认名；子键不覆盖标量，显式 null 不能回退为 fullnet。
            if (!hasConnectionName) connectionName = "fullnet";
            if (string.IsNullOrWhiteSpace(connectionName))
            {
                throw new InvalidOperationException("Connection name is empty.");
            }
            // 保留已有基础凭据字段类型检查；实际取值按宿主的展平路径处理扁平键与大小写。
            foreach (var connectionStrings in ReadNestedConfigurationValues(root, "ConnectionStrings"))
            {
                if (connectionStrings.ValueKind == JsonValueKind.Null) continue;
                foreach (var pair in connectionStrings.EnumerateObject())
                    if (string.Equals(pair.Name, connectionName, StringComparison.OrdinalIgnoreCase))
                        _ = pair.Value.GetString();
            }
            // 显式 null 或空集合也是覆盖值，不能恢复基础文件中的命名凭据。
            if (!TryReadConfigurationOverride(profileSettings, workspacePath, profile,
                    $"ConnectionStrings:{connectionName}", out effectiveConnection))
            {
                _ = TryReadBaseConfigurationValue(root, $"ConnectionStrings:{connectionName}", out effectiveConnection);
            }
        }
        // 默认 WebApplicationBuilder 仅在 Development 载入 User Secrets，生产诊断不能据此放行。
        var userSecretsId = string.Equals(profile, "development", StringComparison.OrdinalIgnoreCase)
            ? TryReadUserSecretsId(workspacePath) : null;

        if (!string.IsNullOrWhiteSpace(effectiveConnection) && !IsPlaceholder(effectiveConnection))
        {
            if (!HasValidConnectionConfiguration(root, profileSettings, workspacePath, profile, effectiveConnection))
            {
                findings.Add(DiagnoseFinding.Error(
                    "DIAG_CONNECTION_INVALID",
                    "所选数据库连接串与最终数据库配置不兼容。",
                    "按 Database:Provider 修正有效直配或命名连接的键名、引号或值类型；MySQL 还须与 Database:MySqlGuidStorageMode 匹配，移除冲突 GuidFormat 或 Old Guids。诊断不会输出连接串或驱动异常，也不会打开连接。"));
                return;
            }
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_CONNECTION_CONFIGURED",
                "所选数据库连接配置已提供；受支持 Provider 的离线校验已通过，未验证地址、认证或数据库可用性。"));
            return;
        }

        if (string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_CONNECTION_MISSING",
                "生产配置缺少所选数据库连接。",
                "通过密钥管理或环境变量 Database__ConnectionString 提供直配；直配为空时核对 Database:ConnectionName 并注入 ConnectionStrings__<name>；不要在仓库中提交凭据。"));
            return;
        }

        // 连接名也可能被错误地填写为凭据；提示固定配置路径，不回显任何来源的字段值。
        var hint = userSecretsId is null
            ? "设置环境变量 Database__ConnectionString；直配为空时核对 Database:ConnectionName 并设置 ConnectionStrings__<name>；如需 user-secrets，先在 API 项目初始化后保存对应连接键。"
            : "使用 user-secrets 或环境变量提供 Database:ConnectionString；直配为空时核对 Database:ConnectionName 并提供 ConnectionStrings:<name>。";
        findings.Add(DiagnoseFinding.Warn(
            "DIAG_CONNECTION_PLACEHOLDER",
            "开发环境尚未配置所选数据库连接。",
            hint));
    }

    private static bool TryReadUserSecret(
        string userSecretsId, string configurationPath, out string? text, bool includeScalarValues = false)
    {
        text = null;
        try
        {
            var path = GetUserSecretsPath(userSecretsId);
            if (!File.Exists(path))
            {
                return false;
            }

            // 按 JSON 配置提供程序的展平顺序读取，允许无关键被空集合覆盖。
            using var document = JsonDocument.Parse(File.ReadAllText(path), ConfigurationJsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return true;
            }
            if (!HasUniqueConfigurationPaths(document.RootElement, null,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
            var found = false;
            VisitConfigurationValue(document.RootElement, null, configurationPath, ref found, ref text, includeScalarValues);
            return found;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 秘密文件不可读取或无效时失败关闭，且不把内容或解析异常写入诊断输出。
            return true;
        }
    }

    private static void VisitConfigurationValue(
        JsonElement element, string? path, string requestedPath, ref bool found, ref string? text,
        bool includeScalarValues = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            if (properties.Length == 0)
            {
                ApplyConfigurationValue(path, requestedPath, null, ref found, ref text);
            }
            foreach (var property in properties)
            {
                // 根路径用 null 区分合法空属性名；空名子项必须保留冒号，不能映射到根配置键。
                VisitConfigurationValue(property.Value, path is null ? property.Name : $"{path}:{property.Name}",
                    requestedPath, ref found, ref text, includeScalarValues);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToArray();
            if (items.Length == 0)
            {
                ApplyConfigurationValue(path, requestedPath, string.Empty, ref found, ref text);
            }
            for (var index = 0; index < items.Length; index++)
            {
                VisitConfigurationValue(items[index], $"{path}:{index}", requestedPath, ref found, ref text, includeScalarValues);
            }
            return;
        }

        // 与 JSON 配置提供程序一致：布尔标量转成 True/False，不能用小写原文误判区分大小写的 KeyId。
        ApplyConfigurationValue(path, requestedPath,
            element.ValueKind == JsonValueKind.String ? element.GetString()
                : includeScalarValues && element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    ? element.ToString() : null, ref found, ref text);
    }

    private static void ApplyConfigurationValue(
        string? path, string requestedPath, string? value, ref bool found, ref string? text)
    {
        if (string.Equals(path, requestedPath, StringComparison.OrdinalIgnoreCase))
        {
            found = true;
            text = value;
        }
    }

    private static readonly JsonDocumentOptions ConfigurationJsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static string GetUserSecretsPath(string userSecretsId) => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", userSecretsId, "secrets.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft", "usersecrets", userSecretsId, "secrets.json");

    private static bool IsValidUserSecretsFile(string userSecretsId)
    {
        var path = GetUserSecretsPath(userSecretsId);
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), ConfigurationJsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && HasUniqueConfigurationPaths(document.RootElement, null,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasUniqueConfigurationPaths(
        JsonElement element, string? path, HashSet<string> paths)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            if (properties.Length == 0)
            {
                // JSON provider 的空对象会写入并覆盖当前路径；仅后续标量遇到同路径才拒绝。
                if (path is not null)
                {
                    _ = paths.Add(path);
                }
                return true;
            }
            foreach (var property in properties)
            {
                var childPath = path is null ? property.Name : $"{path}:{property.Name}";
                if (!HasUniqueConfigurationPaths(property.Value, childPath, paths))
                {
                    return false;
                }
            }
            return true;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToArray();
            if (items.Length == 0)
            {
                if (path is not null)
                {
                    _ = paths.Add(path);
                }
                return true;
            }
            for (var index = 0; index < items.Length; index++)
            {
                if (!HasUniqueConfigurationPaths(items[index], $"{path}:{index}", paths))
                {
                    return false;
                }
            }
            return true;
        }

        // JSON 配置提供程序按不区分大小写的扁平路径读取，扁平键与嵌套键冲突也会阻止启动。
        return path is null || paths.Add(path);
    }

    private static string? GetEnvironmentConfigurationValue(string configurationPath)
    {
        string? selected = null;
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key?.ToString() is not { } rawKey) continue;
            var key = rawKey.Replace("__", ":", StringComparison.Ordinal);
            var value = entry.Value?.ToString();
            // 默认环境提供程序先识别连接前缀，再规范化名称；元数据不改变 Database:Provider。
            foreach (var (prefix, providerName) in ConnectionEnvironmentPrefixes)
            {
                if (!rawKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                key = "ConnectionStrings:" + rawKey[prefix.Length..].Replace("__", ":", StringComparison.Ordinal);
                if (providerName is not null && string.Equals(configurationPath, key + "_ProviderName", StringComparison.OrdinalIgnoreCase))
                {
                    key += "_ProviderName";
                    value = providerName;
                }
                break;
            }
            if (!string.Equals(key, configurationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Linux 可以同时存在仅大小写不同的变量；任一占位值都不能被另一个变量掩盖。
            if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
            {
                return value;
            }
            selected ??= value;
        }
        return selected;
    }

    private static void CheckSecretPlaceholders(
        JsonElement root,
        JsonDocument? profileSettings,
        string workspacePath,
        string profile,
        List<DiagnoseFinding> findings)
    {
        var placeholders = new List<string>();
        foreach (var path in SecretPlaceholderPaths)
        {
            if (TryReadConfigurationOverride(profileSettings, workspacePath, profile, path, out var effectiveValue))
            {
                if (string.IsNullOrWhiteSpace(effectiveValue) || IsPlaceholder(effectiveValue))
                {
                    placeholders.Add(path);
                }
                continue;
            }

            if (IsEmptyPlaceholder(root, path))
            {
                placeholders.Add(path);
            }
        }

        if (placeholders.Count == 0)
        {
            findings.Add(DiagnoseFinding.Ok(
                "DIAG_SECRETS_OK",
                "已配置的常见秘密键无空值或占位符。"));
            return;
        }

        if (string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_SECRETS_PLACEHOLDER",
                $"生产环境仍有 {placeholders.Count} 个秘密占位符。",
                "通过部署密钥或环境变量注入；诊断不会输出秘密值。"));
            return;
        }

        findings.Add(DiagnoseFinding.Warn(
            "DIAG_SECRETS_PLACEHOLDER",
            $"开发环境有 {placeholders.Count} 个秘密仍为空占位符。",
            "通过 user-secrets 或环境变量注入；诊断不会输出秘密值。"));
    }

    private static bool IsEmptyPlaceholder(JsonElement root, string colonPath)
    {
        // 当前三个秘密配置的运行时契约均为字符串；错误类型不能被视为已配置。
        foreach (var current in ReadNestedConfigurationValues(root, colonPath)) _ = current.GetString();

        // 未声明键不强制存在；显式 null 和空集合按展平覆盖结果诊断，不能被层级查找漏掉。
        return TryReadBaseConfigurationValue(root, colonPath, out var text)
            && (string.IsNullOrWhiteSpace(text) || IsPlaceholder(text));
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("<your-", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadUserSecretsId(string workspacePath)
    {
        var standaloneHost = FindStandaloneHost(workspacePath);
        var projectCandidates = new[]
        {
            standaloneHost is null ? null : Path.Combine(standaloneHost,
                Path.GetFileName(standaloneHost) + ".csproj"),
            Path.Combine(workspacePath, "src/App.Host.Api/App.Host.Api.csproj"),
            Path.Combine(workspacePath, "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj"),
        };
        foreach (var candidate in projectCandidates)
        {
            if (candidate is null)
            {
                continue;
            }
            var path = Path.GetFullPath(candidate);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var id = XDocument.Load(path).Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "UserSecretsId")?.Value.Trim();
                // 项目文件可能由外部应用提供；ID 只能映射到 UserSecrets 下的单个目录。
                if (!string.IsNullOrWhiteSpace(id) && !id.Contains("..", StringComparison.Ordinal)
                    && id.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
                {
                    return id;
                }
            }
            catch (Exception exception) when (exception is System.Xml.XmlException or IOException
                or UnauthorizedAccessException)
            {
                return null;
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

    internal sealed record DiagnoseFinding(string Code, string Severity, string Message, string? Hint)
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
