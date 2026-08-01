using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 为数据库会话锁生成稳定、可复现的工作区资源名。
/// </summary>
internal static class CodeGenerationWorkspaceLockResource
{
    private const string Prefix = "fn:codegeneration:workspace:";

    public static string Create(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException(
                "工作区根路径不能为空。",
                nameof(workspaceRoot));
        }

        var normalized = Path.GetFullPath(workspaceRoot)
            .Replace('\\', '/')
            .TrimEnd('/');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant();
    }
}