using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // Identity 是合法模块组合的必需模块；未声明该节时仍须按宿主默认值检查签发前提。
    // 此工具只验证配置前提，不导入 PEM、不创建临时密钥，也不认证整个 Identity Options。
    private static void CheckIdentitySigning(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        string? Read(string path)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, path, out var value);
            return value;
        }

        var hasEndpoints = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
            "Identity:EnableTokenEndpoints", out var endpoints);
        var ephemeral = Read("Identity:AllowDevelopmentEphemeralSigningKey");
        if ((endpoints is not null && !bool.TryParse(endpoints, out _))
            || (ephemeral is not null && !bool.TryParse(ephemeral, out _)))
        {
            findings.Add(DiagnoseFinding.Error(
                "DIAG_IDENTITY_SIGNING_OPTIONS_INVALID",
                "Identity 签名相关开关不能绑定为布尔值。",
                "将 EnableTokenEndpoints 与 AllowDevelopmentEphemeralSigningKey 配置为 true 或 false；诊断不会输出配置值。"));
            return;
        }

        // .NET 10 将显式 JSON null（含空对象）绑定为 bool 默认值 false，缺键才保留 Options 的 true。
        var tokenEndpointsEnabled = !hasEndpoints || (endpoints is not null && bool.Parse(endpoints));
        var ephemeralEnabled = ephemeral is not null && bool.Parse(ephemeral);
        var production = string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase);
        // 即使关闭签发端点，Production 也不能配置开发临时密钥；与宿主校验顺序一致。
        if (ephemeralEnabled)
        {
            var configuredKeys = ReadIdentitySigningKeyNames(root, profileSettings, workspacePath, profile).ToArray();
            if (!production && configuredKeys.Length > 0)
            {
                CheckDevelopmentConfiguredSigningKeys(root, profileSettings, workspacePath, profile,
                    false, configuredKeys, findings);
                return;
            }

            findings.Add(production
                ? DiagnoseFinding.Error("DIAG_IDENTITY_EPHEMERAL_SIGNING",
                    "Production 禁止启用开发临时签名密钥。",
                    "关闭 AllowDevelopmentEphemeralSigningKey；需要签发令牌时配置持久签名密钥。")
                : DiagnoseFinding.Warn("DIAG_IDENTITY_EPHEMERAL_SIGNING",
                    "Development 已显式启用临时签名密钥；重启后令牌失效，不能用于生产。",
                    "共享实例或持久会话请配置活动签名密钥；诊断不会生成或输出密钥。"));
            return;
        }

        if (!tokenEndpointsEnabled)
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_IDENTITY_TOKEN_ENDPOINTS_DISABLED",
                "Identity 令牌端点已关闭；未要求签发用的活动密钥。"));
            return;
        }

        var active = Read("Identity:ActiveKeyId");
        // 配置路径大小写不敏感，但宿主签名密钥字典按 Ordinal 匹配 KeyId；不能仅拼路径取值。
        var names = ReadIdentitySigningKeyNames(root, profileSettings, workspacePath, profile);
        var configured = !string.IsNullOrWhiteSpace(active) && !IsPlaceholder(active)
            && names.Contains(active, StringComparer.Ordinal)
            && IsUsableSigningValue(Read("Identity:SigningKeys:" + active + ":PublicKeyPem"))
            && IsUsableSigningValue(Read("Identity:SigningKeys:" + active + ":PrivateKeyPem"));
        if (configured)
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_IDENTITY_SIGNING_CONFIGURED",
                "Identity 活动签名密钥配置项齐全；未验证 PEM 格式或密码学有效性。"));
            return;
        }

        const string message = "Identity 令牌端点缺少匹配且完整的活动签名密钥配置，宿主无法按此配置签发令牌。";
        const string hint = "配置 ActiveKeyId 及同名 SigningKeys 的 PublicKeyPem、PrivateKeyPem；仅 Development 可显式启用 AllowDevelopmentEphemeralSigningKey。诊断不会输出 KeyId 或密钥。";
        findings.Add(production
            ? DiagnoseFinding.Error("DIAG_IDENTITY_SIGNING_REQUIRED", message, hint)
            : DiagnoseFinding.Warn("DIAG_IDENTITY_SIGNING_REQUIRED", message, hint));
    }

    private static bool IsUsableSigningValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value);

    private static bool TryReadIdentitySigningValue(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        string path, out string? value)
    {
        if (TryReadConfigurationOverride(profileSettings, workspacePath, profile, path, out value,
                requireValidUserSecrets: true, includeScalarValues: true)) return true;
        return TryReadBaseConfigurationValue(root, path, out value, includeScalarValues: true);
    }

    private static IEnumerable<string> ReadIdentitySigningKeyNames(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        string sectionPath = "Identity")
    {
        var prefix = sectionPath + ":SigningKeys:";
        return ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile)
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path[prefix.Length..].Split(':')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadRuntimeConfigurationPaths(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile)
    {
        // 宿主 GetChildren 以高优先级提供程序的键名拼写合并重复子键；值仍逐叶覆盖，空父节点不删除子键。
        var paths = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key?.ToString() is { } key) paths.Add(key.Replace("__", ":", StringComparison.Ordinal));
        if (string.Equals(profile, "development", StringComparison.OrdinalIgnoreCase)
            && TryReadUserSecretsId(workspacePath) is { } id && IsValidUserSecretsFile(id))
        {
            var secretsPath = GetUserSecretsPath(id);
            if (File.Exists(secretsPath))
            {
                using var secrets = ReadConfigurationDocument(secretsPath);
                paths.AddRange(EnumerateConfigurationLeaves(secrets.RootElement, null).Select(leaf => leaf.Path));
            }
        }
        if (profileSettings is not null)
            paths.AddRange(EnumerateConfigurationLeaves(profileSettings.RootElement, null).Select(leaf => leaf.Path));
        paths.AddRange(EnumerateConfigurationLeaves(root, null).Select(leaf => leaf.Path));
        return paths;
    }
}
