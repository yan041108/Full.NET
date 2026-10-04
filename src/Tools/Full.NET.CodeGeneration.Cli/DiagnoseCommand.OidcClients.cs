using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 只核对固定客户端的标识与回调白名单；不启动协议、不联系地址，也不输出客户端秘密。
    private static void CheckOidcClients(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        string? Read(string path)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, path, out var value);
            return value;
        }

        if (!bool.TryParse(Read("Identity:Oidc:Enable"), out var enabled) || !enabled) return;

        var paths = ReadIdentityConfigurationPaths(root, profileSettings, workspacePath, profile);
        string[] Children(string path)
        {
            var prefix = path + ":";
            return paths.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(key => key[prefix.Length..].Split(':')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        bool ValidUris(string path, bool required)
        {
            var children = Children(path);
            if (children.Length == 0) return !required;
            var count = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in children)
            {
                var itemPath = path + ":" + child;
                var hasValue = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, itemPath, out var value);
                // 字符串数组忽略不能构造字符串的对象项；显式 null 项仍须按无效 URI 拒绝。
                if (!hasValue && Children(itemPath).Length > 0) continue;
                count++;
                if (string.IsNullOrWhiteSpace(value) || value.Contains('*', StringComparison.Ordinal)
                    || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)
                    || !seen.Add(value.TrimEnd('/'))) return false;
            }
            return !required || count > 0;
        }

        const string clientsPath = "Identity:Oidc:Clients";
        var count = 0;
        var valid = true;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in Children(clientsPath))
        {
            var path = clientsPath + ":" + child;
            if (Children(path).Length == 0)
            {
                // Binder 忽略不能构造客户端对象的非空标量，却会保留 null 项；与真实绑定回归对照。
                if (Read(path) is not null) continue;
                valid = false;
                break;
            }
            // Binder 在数组内跳过布尔属性转换失败的整个客户端，不能把该项纳入有效数量或去重。
            var firstParty = Read(path + ":IsFirstParty");
            if (firstParty is not null && !bool.TryParse(firstParty, out _)) continue;
            count++;
            var id = Read(path + ":ClientId");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)
                || !ValidUris(path + ":RedirectUris", required: true)
                || !ValidUris(path + ":PostLogoutRedirectUris", required: false))
            {
                valid = false;
                break;
            }
        }

        findings.Add(valid && count > 0
            ? DiagnoseFinding.Ok("DIAG_OIDC_CLIENTS_CONFIGURED",
                "OIDC 客户端标识和回调白名单已通过配置检查；未认证客户端秘密、其他选项或完整协议启动。")
            : DiagnoseFinding.Error("DIAG_OIDC_CLIENTS_INVALID",
                "已启用的 OIDC 缺少有效客户端，或客户端标识、回调白名单无效。",
                "配置 Identity:Oidc:Clients，使用非空且区分大小写唯一的 ClientId；RedirectUris 必填，回调须为精确 HTTP(S) 绝对地址，不含通配符、凭据或片段，尾斜线归一后不得重复。诊断不会输出客户端标识、地址或秘密。"));
    }
}
