using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 为数据库会话锁生成稳定、可复现的工作区资源名。
/// </summary>
internal static class CodeGenerationWorkspaceLockResource
{
    private const string Prefix = "fn:codegeneration:workspace:";

    /// <summary>
    /// 基于工作区根路径生成稳定、可复现的锁资源名：先归一为绝对路径并用正斜杠、去尾斜杠，
    /// 再取 SHA-256 小写十六进制，保证多实例对同一工作区得到相同资源名，从而形成真实互斥。
    /// </summary>
    /// <param name="workspaceRoot">配置的本地工作区根目录，不能为空白。</param>
    /// <exception cref="ArgumentException">工作区根路径为空白时抛出。</exception>
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