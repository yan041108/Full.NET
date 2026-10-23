using System.Security.Cryptography;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>轻量 DOCX 执行配置只来自可信宿主；路径和镜像不接受业务输入。</summary>
internal sealed record AiKnowledgeDocxSandboxOptions(string NodeExecutablePath, string CliScriptPath, string ImageId, int TimeoutMs = 25000);

/// <summary>原生 DOCX 工具的协议适配器；通用进程边界确认退出和清理后才消费正文。</summary>
internal sealed class AiKnowledgeDocxSandboxParser(IDataTransactionState transactions, AiKnowledgeDocxSandboxOptions options)
{
    internal Task<AiKnowledgeParsedDocx?> ParseAsync(ReadOnlyMemory<byte> authorizedSource, string trustedHash, int trustedSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return AiKnowledgeTelemetry.RunAsync("knowledge_parse_docx", () => ParseCoreAsync(authorizedSource, trustedHash, trustedSize, token), token);
    }

    private async Task<AiKnowledgeParsedDocx?> ParseCoreAsync(ReadOnlyMemory<byte> authorizedSource, string trustedHash, int trustedSize, CancellationToken token)
    {
        var process = new AiKnowledgeSandboxProcess(transactions, new(options.NodeExecutablePath, options.CliScriptPath, options.ImageId,
            AiKnowledgeDocxOutputReader.MaxSourceBytes, options.TimeoutMs, 30000));
        var output = await process.ReadAsync(authorizedSource, trustedHash, trustedSize, token).ConfigureAwait(false);
        if (output is null) return null;
        try
        {
            using var wire = new MemoryStream(output, false);
            return (await AiKnowledgeDocxOutputReader.ReadAsync(wire, trustedHash, trustedSize, token).ConfigureAwait(false)).Document;
        }
        finally { CryptographicOperations.ZeroMemory(output); }
    }
}
