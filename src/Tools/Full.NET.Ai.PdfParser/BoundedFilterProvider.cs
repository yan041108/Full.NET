using System.Buffers.Binary;
using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Full.NET.Ai.PdfParser;

/// <summary>只支持可有界解码的文本压缩，不调用默认无界 Flate。</summary>
internal sealed class BoundedFilterProvider : IFilterProvider
{
    internal const int MaxDecodedBytes = 32 * 1024 * 1024;
    private int decodedBytes;
    private string? failure;

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
    {
        EnsureHealthy();
        if (!dictionary.Data.TryGetValue(NameToken.F, out var token)
            && !dictionary.Data.TryGetValue(NameToken.Filter, out token)) return [];
        return token switch
        {
            NameToken name => GetNamedFilters([name]),
            ArrayToken array when array.Data.All(item => item is NameToken) => GetNamedFilters(array.Data.Cast<NameToken>().ToArray()),
            _ => throw Reject("fn_ai_pdf_unsupported_filter")
        };
    }
    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
    {
        EnsureHealthy();
        if (names.Count > 8) throw Reject("fn_ai_pdf_unsupported_filter");
        return names.Select(name => name.Data switch
        {
            "FlateDecode" or "Fl" => (IFilter)new BoundedFilter(this, true),
            "ASCIIHexDecode" or "AHx" => new BoundedFilter(this, false),
            _ => throw Reject("fn_ai_pdf_unsupported_filter")
        }).ToArray();
    }
    public IReadOnlyList<IFilter> GetAllFilters() => [new BoundedFilter(this, true), new BoundedFilter(this, false)];
    internal void EnsureHealthy()
    {
        if (failure is not null) throw new PdfRejectedException(failure);
    }
    private PdfRejectedException Reject(string code)
    {
        failure ??= code;
        return new PdfRejectedException(failure);
    }
    private void Charge(int bytes)
    {
        if (bytes > MaxDecodedBytes - decodedBytes) throw Reject("fn_ai_pdf_decode_limit");
        decodedBytes += bytes;
    }

    private sealed class BoundedFilter(BoundedFilterProvider owner, bool flate) : IFilter
    {
        public bool IsSupported => true;
        public Memory<byte> Decode(Memory<byte> input, DictionaryToken dictionary, IFilterProvider provider, int index)
        {
            owner.EnsureHealthy();
            try
            {
                var parameters = GetParameters(dictionary, index);
                if (parameters is not null && parameters.Data.TryGetValue(NameToken.Predictor, out var predictor)
                    && (predictor is not NumericToken numeric || numeric.Data != 1))
                    throw owner.Reject("fn_ai_pdf_unsupported_predictor");
                return flate ? Inflate(input) : DecodeHex(input.Span);
            }
            catch (PdfRejectedException exception) { throw owner.Reject(exception.Code); }
            catch (Exception) { throw owner.Reject("fn_ai_pdf_corrupt_stream"); }
        }

        private DictionaryToken? GetParameters(DictionaryToken dictionary, int index)
        {
            if (!dictionary.Data.TryGetValue(NameToken.Dp, out var parameters)
                && !dictionary.Data.TryGetValue(NameToken.DecodeParms, out parameters)) return null;
            if (parameters is NullToken) return null;
            if (!dictionary.Data.TryGetValue(NameToken.Filter, out var filter)) dictionary.Data.TryGetValue(NameToken.F, out filter);
            // 不可解析的间接引用不能变成空参数；此受控子集明确拒绝，而非忽略预测器。
            if (filter is ArrayToken filters && parameters is ArrayToken array && filters.Data.Count == array.Data.Count
                && index >= 0 && index < array.Data.Count) parameters = array.Data[index];
            else if (filter is not NameToken || index != 0) throw owner.Reject("fn_ai_pdf_unsupported_predictor");
            return parameters switch
            {
                NullToken => null,
                DictionaryToken direct => direct,
                _ => throw owner.Reject("fn_ai_pdf_unsupported_predictor")
            };
        }

        private Memory<byte> Inflate(Memory<byte> input)
        {
            if (input.Length < 6 || (input.Span[0] & 15) != 8 || (input.Span[0] >> 4) > 7
                || ((input.Span[0] << 8) + input.Span[1]) % 31 != 0 || (input.Span[1] & 32) != 0)
                throw owner.Reject("fn_ai_pdf_corrupt_stream");
            var expectedBytes = DeflateFraming.Validate(input.Span[2..^4], MaxDecodedBytes - owner.decodedBytes);
            using var compressed = new MemoryStream(input.ToArray(), false);
            using var decoder = new ZLibStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            uint first = 1, second = 0;
            while (true)
            {
                var count = decoder.Read(buffer, 0, Math.Min(buffer.Length, MaxDecodedBytes - owner.decodedBytes + 1));
                if (count == 0) break;
                // 读入至多剩余预算加一；在追加和扩大输出缓冲之前记账。
                owner.Charge(count);
                output.Write(buffer, 0, count);
                for (var offset = 0; offset < count; offset++)
                {
                    first = (first + buffer[offset]) % 65521;
                    second = (second + first) % 65521;
                }
            }
            // ZLibStream 对部分截断可能只返回 EOF，显式核对尾部 Adler32，禁止原始输入回退。
            if (output.Length != expectedBytes || BinaryPrimitives.ReadUInt32BigEndian(input.Span[^4..]) != ((second << 16) | first))
                throw owner.Reject("fn_ai_pdf_corrupt_stream");
            return output.ToArray();
        }

        private Memory<byte> DecodeHex(ReadOnlySpan<byte> input)
        {
            using var output = new MemoryStream();
            var high = -1;
            var ended = false;
            foreach (var character in input)
            {
                if (character is 0 or 9 or 10 or 12 or 13 or 32) continue;
                if (ended) throw owner.Reject("fn_ai_pdf_corrupt_stream");
                if (character == '>')
                {
                    if (high >= 0) { owner.Charge(1); output.WriteByte((byte)(high << 4)); }
                    ended = true;
                    continue;
                }
                var nibble = character switch
                {
                    >= (byte)'0' and <= (byte)'9' => character - '0',
                    >= (byte)'a' and <= (byte)'f' => character - 'a' + 10,
                    >= (byte)'A' and <= (byte)'F' => character - 'A' + 10,
                    _ => -1
                };
                if (nibble < 0) throw owner.Reject("fn_ai_pdf_corrupt_stream");
                if (high < 0) high = nibble;
                else { owner.Charge(1); output.WriteByte((byte)((high << 4) | nibble)); high = -1; }
            }
            if (!ended) throw owner.Reject("fn_ai_pdf_corrupt_stream");
            return output.ToArray();
        }
    }
}
