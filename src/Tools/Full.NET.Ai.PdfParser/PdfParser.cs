using System.Security.Cryptography;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Full.NET.Ai.PdfParser;

/// <summary>独立进程中的固定失败码；异常正文永不跨越进程边界。</summary>
internal sealed class PdfRejectedException(string code) : Exception(code)
{
    internal string Code { get; } = code;
}

/// <summary>只解析一份有界文件；硬时间、内存、网络与取消由进程外边界负责。</summary>
internal static class PdfParser
{
    internal const int MaxInputBytes = 5 * 1024 * 1024;
    internal const string Version = "pdfpig-0.1.16-content-order-v1";
    internal static async Task<int> RunAsync(Stream input, Stream output, TextWriter errors)
    {
        var filters = new BoundedFilterProvider();
        try
        {
            // 不信任 Length，最多读入上限加一个探测字节；标准流由调用者持有。
            using var source = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxInputBytes + 1 - (int)source.Length)));
                if (count == 0) break;
                source.Write(buffer, 0, count);
                if (source.Length > MaxInputBytes) throw new PdfRejectedException("fn_ai_pdf_input_too_large");
            }
            var bytes = source.ToArray();
            if (!HasSignature(bytes)) throw new PdfRejectedException("fn_ai_pdf_bad_signature");
            using var document = PdfDocument.Open(bytes, new ParsingOptions
            {
                UseLenientParsing = false,
                SkipMissingFonts = false,
                MaxStackDepth = 64,
                FilterProvider = filters
            });
            filters.EnsureHealthy();
            if (document.IsEncrypted) throw new PdfRejectedException("fn_ai_pdf_encrypted");
            if (document.NumberOfPages > PdfOutput.MaxPages) throw new PdfRejectedException("fn_ai_pdf_page_limit");
            var pages = new PdfOutput();
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                var text = ContentOrderTextExtractor.GetText(document.GetPage(number));
                // PdfPig 的容错路径也不得清除预算或未知编码错误。
                filters.EnsureHealthy();
                pages.Add(number, text);
            }
            filters.EnsureHealthy();
            pages.Write(Convert.ToHexStringLower(SHA256.HashData(bytes)), output);
            await output.FlushAsync();
            return 0;
        }
        catch (Exception exception)
        {
            var code = exception is PdfRejectedException rejected ? rejected.Code : "fn_ai_pdf_parse_failed";
            try { filters.EnsureHealthy(); }
            catch (PdfRejectedException filterError) { code = filterError.Code; }
            await errors.WriteLineAsync(code);
            return 2;
        }
    }

    private static bool HasSignature(byte[] bytes)
        => bytes.Length >= 9 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            && ((bytes[5] == '1' && bytes[6] == '.' && bytes[7] is >= (byte)'0' and <= (byte)'7')
                || (bytes[5] == '2' && bytes[6] == '.' && bytes[7] == '0'))
            && bytes[8] is 10 or 13 or 32;
}
