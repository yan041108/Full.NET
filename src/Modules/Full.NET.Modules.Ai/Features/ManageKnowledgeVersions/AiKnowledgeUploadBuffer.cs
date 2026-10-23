using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>在保存导入意图前校验实际字节；不信任 Content-Length，也不解析或派发模型。</summary>
internal sealed class AiKnowledgeUploadBuffer(MemoryStream content, string hash) : IDisposable
{
    internal MemoryStream Content { get; } = content;
    internal string Hash { get; } = hash;
    internal long Size => Content.Length;
    public void Dispose() => Content.Dispose();

    internal static async Task<Result<AiKnowledgeUploadBuffer>> ReadAsync(Stream source, string type, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!AiKnowledgeFilePolicy.ContentTypes.Contains(type, StringComparer.Ordinal)) return Invalid();
        var output = new MemoryStream();
        try
        {
            var bytes = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
            {
                if (output.Length > AiKnowledgeFilePolicy.Limit(type) - count)
                {
                    output.Dispose();
                    return Invalid();
                }
                output.Write(bytes, 0, count);
            }
            if (output.Length == 0) { output.Dispose(); return Invalid(); }
            var data = output.GetBuffer();
            var length = checked((int)output.Length);
            if (type == "application/pdf")
            {
                if (length < 5 || !data.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) { output.Dispose(); return Invalid(); }
            }
            else if (type is AiKnowledgeFilePolicy.DocxContentType or AiKnowledgeFilePolicy.XlsxContentType or AiKnowledgeFilePolicy.PptxContentType)
            {
                // 这里只检查 ZIP 外壳；完整结构、压缩比和外部关系由独立隔离解析器检查。
                if (length < 4 || !data.AsSpan(0, 4).SequenceEqual("PK\u0003\u0004"u8)) { output.Dispose(); return Invalid(); }
            }
            else if (type is "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint")
            {
                if (length < 8 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 })) { output.Dispose(); return Invalid(); }
            }
            else if (type is "text/plain" or "text/markdown")
            {
                try { _ = new UTF8Encoding(false, true).GetCharCount(data, 0, length); }
                catch (DecoderFallbackException) { output.Dispose(); return Invalid(); }
            }
            var hash = Convert.ToHexString(SHA256.HashData(data.AsSpan(0, length)));
            output.Position = 0;
            return Result<AiKnowledgeUploadBuffer>.Success(new(output, hash));
        }
        catch { output.Dispose(); throw; }
    }

    private static Result<AiKnowledgeUploadBuffer> Invalid() => Result<AiKnowledgeUploadBuffer>.Failure(
        new Error("ai.knowledge.upload_invalid", "The content is empty, invalid or exceeds the format size limit.", ErrorType.Validation));
}
