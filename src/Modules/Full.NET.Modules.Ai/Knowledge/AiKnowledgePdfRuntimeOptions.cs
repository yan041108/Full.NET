namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>PDF 显式启用边界；路径与不可变镜像只来自宿主可信配置。</summary>
internal sealed class AiKnowledgePdfRuntimeOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgePdf";
    /// <summary>默认关闭；API 与 Worker 必须采用同一部署启用策略。</summary>
    public bool Enabled { get; set; }
    /// <summary>Worker 本机 Node 可执行文件的绝对路径。</summary>
    public string NodeExecutablePath { get; set; } = string.Empty;
    /// <summary>受部署保护的隔离控制器脚本绝对路径。</summary>
    public string CliScriptPath { get; set; } = string.Empty;
    /// <summary>固定 sha256 镜像标识，禁止可变标签。</summary>
    public string ImageId { get; set; } = string.Empty;

    internal bool Supports(string contentType) => contentType is "text/plain" or "text/markdown"
        || (Enabled && contentType == "application/pdf");

    // API 校验部署配置形状；只有 Worker 校验本机文件，API 不需要解析器或 Docker 控制权限。
    internal bool IsValid(bool worker) => !Enabled || (Path.IsPathFullyQualified(NodeExecutablePath)
        && Path.IsPathFullyQualified(CliScriptPath) && ImageId is { Length: 71 }
        && ImageId.StartsWith("sha256:", StringComparison.Ordinal)
        && ImageId.Skip(7).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && (!worker || (File.Exists(NodeExecutablePath) && File.Exists(CliScriptPath))));
}
