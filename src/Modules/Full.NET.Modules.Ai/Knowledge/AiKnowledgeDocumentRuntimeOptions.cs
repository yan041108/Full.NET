using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>多格式和 OCR 单独启用、隔离及预算；不复用轻量 PDF 的内存和执行期限。</summary>
internal sealed class AiKnowledgeDocumentRuntimeOptions
{
    internal const string SectionName = "FullNet:Ai:KnowledgeDocuments";
    /// <summary>默认关闭；API 和 Worker 使用同一部署启用策略。</summary>
    public bool Enabled { get; set; }
    /// <summary>Worker 本机受保护的 Node 绝对路径。</summary>
    public string NodeExecutablePath { get; set; } = string.Empty;
    /// <summary>固定 document-sandbox-cli.mjs 的绝对路径，不接受业务参数覆盖。</summary>
    public string CliScriptPath { get; set; } = string.Empty;
    /// <summary>包含固定模型、字体和编解码器的 sha256 镜像标识。</summary>
    public string ImageId { get; set; } = string.Empty;
    /// <summary>单次解析的总期限，单位毫秒，范围 1000–120000。</summary>
    public int TimeoutMs { get; set; } = 120000;
    /// <summary>每个 Worker 进程最多同时运行的解析数，范围 1–2；每个任务另占 3 GiB/2 CPU。</summary>
    public int MaximumConcurrentParses { get; set; } = 1;

    internal bool Supports(string type) => Enabled && AiKnowledgeDocumentFormats.IsDocument(type);
    internal bool IsValid(bool worker) => !Enabled || (TimeoutMs is >= 1000 and <= 120000 && MaximumConcurrentParses is >= 1 and <= 2
        && Path.IsPathFullyQualified(NodeExecutablePath) && Path.IsPathFullyQualified(CliScriptPath)
        && ImageId is { Length: 71 } && ImageId.StartsWith("sha256:", StringComparison.Ordinal)
        && ImageId.Skip(7).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && (!worker || File.Exists(NodeExecutablePath) && File.Exists(CliScriptPath)));
}

/// <summary>实例内解析准入不捕获请求作用域；排队时不持有原文件正文。</summary>
internal sealed class AiKnowledgeDocumentParseGate(IOptions<AiKnowledgeDocumentRuntimeOptions> options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(options.Value.Enabled ? options.Value.MaximumConcurrentParses : 1, options.Value.Enabled ? options.Value.MaximumConcurrentParses : 1);
    internal async Task<IDisposable> EnterAsync(CancellationToken token)
    { await gate.WaitAsync(token).ConfigureAwait(false); return new Lease(gate); }
    public void Dispose() => gate.Dispose();
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) gate.Release(); }
    }
}
