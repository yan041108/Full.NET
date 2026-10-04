namespace Full.NET.Ai.PdfParser;

/// <summary>按 RFC 1951 验证最终块与有界展开长度；不存储或复制解压正文。</summary>
internal static class DeflateFraming
{
    // 格式常数来自 https://www.rfc-editor.org/rfc/rfc1951.html §3.2，未复制第三方实现。
    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly int[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly int[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
    private static readonly Huffman FixedLiteral = new(Enumerable.Range(0, 288).Select(symbol => symbol switch { <= 143 => 8, <= 255 => 9, <= 279 => 7, _ => 8 }).ToArray());
    private static readonly Huffman FixedDistance = new(Enumerable.Repeat(5, 32).ToArray());

    internal static int Validate(ReadOnlySpan<byte> payload, int budget)
    {
        var bits = new Bits(payload);
        var expanded = 0;
        bool final;
        do
        {
            final = bits.Read(1) == 1;
            var type = bits.Read(2);
            if (type == 0)
            {
                bits.Align();
                var length = bits.Read(16);
                if ((length ^ bits.Read(16)) != 65535) throw Corrupt();
                Grow(ref expanded, length, budget);
                bits.SkipBytes(length);
                continue;
            }
            if (type == 3) throw Corrupt();
            var literal = FixedLiteral;
            var distance = FixedDistance;
            if (type == 2) ReadDynamic(ref bits, out literal, out distance);
            while (true)
            {
                var symbol = literal.Read(ref bits);
                if (symbol < 256) { Grow(ref expanded, 1, budget); continue; }
                if (symbol == 256) break;
                if (symbol > 285) throw Corrupt();
                var length = LengthBase[symbol - 257] + bits.Read(LengthExtra[symbol - 257]);
                var distanceSymbol = distance.Read(ref bits);
                if (distanceSymbol >= DistanceBase.Length) throw Corrupt();
                var extra = distanceSymbol < 4 ? 0 : distanceSymbol / 2 - 1;
                var back = DistanceBase[distanceSymbol] + bits.Read(extra);
                if (back > expanded) throw Corrupt();
                Grow(ref expanded, length, budget);
            }
        } while (!final);
        // 最终 Huffman 块允许字节内剩余填充位，不允许另一整字节或伪造的后续帧。
        if (bits.Remaining >= 8) throw Corrupt();
        return expanded;
    }

    private static void Grow(ref int expanded, int count, int budget)
    {
        if (count > budget - expanded) throw new PdfRejectedException("fn_ai_pdf_decode_limit");
        expanded += count;
    }

    private static void ReadDynamic(ref Bits bits, out Huffman literal, out Huffman distance)
    {
        var literalCount = bits.Read(5) + 257;
        var distanceCount = bits.Read(5) + 1;
        var codeCount = bits.Read(4) + 4;
        if (literalCount > 286) throw Corrupt();
        var codeLengths = new int[19];
        for (var index = 0; index < codeCount; index++) codeLengths[CodeLengthOrder[index]] = bits.Read(3);
        var codes = new Huffman(codeLengths);
        var lengths = new int[literalCount + distanceCount];
        for (var index = 0; index < lengths.Length;)
        {
            var symbol = codes.Read(ref bits);
            if (symbol <= 15) { lengths[index++] = symbol; continue; }
            var repeated = 0;
            var count = symbol switch
            {
                16 when index > 0 => bits.Read(2) + 3,
                17 => bits.Read(3) + 3,
                18 => bits.Read(7) + 11,
                _ => throw Corrupt()
            };
            if (symbol == 16) repeated = lengths[index - 1];
            if (count > lengths.Length - index) throw Corrupt();
            lengths.AsSpan(index, count).Fill(repeated);
            index += count;
        }
        if (lengths[256] == 0) throw Corrupt();
        literal = new Huffman(lengths.AsSpan(0, literalCount));
        distance = new Huffman(lengths.AsSpan(literalCount));
    }

    private sealed class Huffman
    {
        private readonly Dictionary<int, int> symbols = [];
        private readonly int maximum;
        internal Huffman(ReadOnlySpan<int> lengths)
        {
            Span<int> counts = stackalloc int[16];
            Span<int> next = stackalloc int[16];
            foreach (var length in lengths)
            {
                if (length is < 0 or > 15) throw Corrupt();
                if (length > 0) { counts[length]++; maximum = Math.Max(maximum, length); }
            }
            var available = 1;
            for (var length = 1; length <= 15; length++)
            {
                available = available * 2 - counts[length];
                if (available < 0) throw Corrupt();
                next[length] = (next[length - 1] + counts[length - 1]) << 1;
            }
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];
                if (length > 0) symbols.Add((1 << length) | next[length]++, symbol);
            }
        }
        internal int Read(ref Bits bits)
        {
            var code = 0;
            // Huffman 码按高位先行；固定宽度字段则按低位先行，不能共用整数读法。
            for (var length = 1; length <= maximum; length++)
            {
                code = (code << 1) | bits.Read(1);
                if (symbols.TryGetValue((1 << length) | code, out var symbol)) return symbol;
            }
            throw Corrupt();
        }
    }

    private ref struct Bits(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> bytes = bytes;
        private int position;
        internal readonly int Remaining => bytes.Length * 8 - position;
        internal int Read(int count)
        {
            if (count > Remaining) throw Corrupt();
            var value = 0;
            for (var index = 0; index < count; index++, position++) value |= ((bytes[position >> 3] >> (position & 7)) & 1) << index;
            return value;
        }
        internal void Align() => position = (position + 7) & ~7;
        internal void SkipBytes(int count)
        {
            if (count > Remaining / 8) throw Corrupt();
            position += count * 8;
        }
    }
    private static PdfRejectedException Corrupt() => new("fn_ai_pdf_corrupt_stream");
}
