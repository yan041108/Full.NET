using System.Security.Cryptography;
using System.Xml;

namespace Full.NET.Ai.DocxParser;

/// <summary>固定错误码，不允许解析异常携带来源、路径或文档内容跨越进程边界。</summary>
internal sealed class DocxRejectedException(string code) : Exception(code)
{
    internal string Code { get; } = code;
}

/// <summary>单份有界 OOXML 正文解析；硬时限、内存和网络隔离由进程外运行器承担。</summary>
internal static class DocxParser
{
    internal const int MaxInputBytes = 5 * 1024 * 1024;
    internal const string Version = "docx-ooxml-body-v1";

    /// <summary>不接受文件路径，完整验证后才输出协议；输入输出流归调用者，取消直接传播。</summary>
    internal static async Task<int> RunAsync(Stream input, Stream output, TextWriter errors, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var source = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // 不信任流 Length，只读取固定上限加一个探测字节，不截断后冒充完整文件。
                var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxInputBytes + 1 - (int)source.Length)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (count == 0) break;
                source.Write(buffer, 0, count);
                if (source.Length > MaxInputBytes) throw new DocxRejectedException("fn_ai_docx_input_limit");
            }
            var bytes = source.ToArray();
            var parts = await DocxArchive.ReadAsync(bytes, token).ConfigureAwait(false);
            DocxXml.ValidatePackage(parts, token);
            var body = DocxXml.Read(parts["word/document.xml"], token);
            var document = DocxBody.Parse(body, token);
            var wire = document.Serialize(Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length, token);
            token.ThrowIfCancellationRequested();
            await output.WriteAsync(wire, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            var code = exception switch
            {
                DocxRejectedException rejected => rejected.Code,
                InvalidDataException => "fn_ai_docx_invalid_package",
                XmlException => "fn_ai_docx_invalid_xml",
                _ => "fn_ai_docx_parse_failed",
            };
            await errors.WriteLineAsync(code).ConfigureAwait(false);
            return 2;
        }
    }
}
