using System.Security.Cryptography;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>多格式/OCR 独立资源档；不可变镜像、程序和期限只能由可信部署配置提供。</summary>
internal sealed record AiKnowledgeDocumentSandboxOptions(string NodeExecutablePath, string CliScriptPath, string ImageId, int TimeoutMs = 120000, bool Enabled = true);
/// <summary>只携带可公开的固定解析失败类别；不包含文件名、正文或解析器异常。</summary>
internal sealed class AiKnowledgeDocumentParsingException(AiKnowledgeIndexFailure failure) : Exception("ai.knowledge.parsing_rejected")
{
    internal AiKnowledgeIndexFailure Failure { get; } = failure;
}

/// <summary>统一隔离解析桥接；版本中固定的策略和原始指纹由调用方的授权读取边界提供。</summary>
internal sealed class AiKnowledgeDocumentSandboxParser(IDataTransactionState transactions, AiKnowledgeDocumentSandboxOptions options)
{
    internal Task<AiKnowledgeParsedDocument?> ParseAsync(ReadOnlyMemory<byte> source, string hash, int size,
        string contentType, string policyKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return AiKnowledgeTelemetry.RunAsync("knowledge_parse_document", () => ParseCoreAsync(source, hash, size, contentType, policyKey, token), token);
    }

    private async Task<AiKnowledgeParsedDocument?> ParseCoreAsync(ReadOnlyMemory<byte> source, string hash, int size,
        string contentType, string policyKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!options.Enabled) return null;
        if (transactions.HasTransaction) throw new InvalidOperationException("fn_ai_parser_process_transaction_forbidden");
        var policy = AiKnowledgeDocumentFormats.Policy(contentType, policyKey);
        if (AiKnowledgeDocumentFormats.Format(contentType) is not { } format || policy is null || !AiKnowledgeDocumentFormats.IsDocument(contentType)
            || size > Domain.AiKnowledgeFilePolicy.Limit(contentType)) return null;
        string[] arguments = ["--format", policy.InputFormat, "--encoding", policy.TextEncoding ?? "utf-8",
            "--delimiter", policy.CsvDelimiter switch { ";" => "semicolon", "\t" => "tab", _ => "comma" },
            "--header", policy.CsvHeader ? "yes" : "no", "--hidden", policy.IncludeHidden ? "include" : "exclude"];
        var process = new AiKnowledgeSandboxProcess(transactions, new(options.NodeExecutablePath, options.CliScriptPath,
            options.ImageId, checked((int)Domain.AiKnowledgeFilePolicy.MaxBytes), options.TimeoutMs, 120000, arguments));
        var output = await process.ReadAsync(source, hash, size, token).ConfigureAwait(false);
        if (output is null)
        {
            if (process.FailureCode == "no_text") throw new AiKnowledgeDocumentParsingException(AiKnowledgeIndexFailure.NoExtractedText);
            return null;
        }
        try { return AiKnowledgeParsedWireCodec.Decode(output, format, hash, size, token, policy); }
        finally { CryptographicOperations.ZeroMemory(output); }
    }
}
