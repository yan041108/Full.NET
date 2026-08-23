using System.Globalization;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers.Domain;

/// <summary>
/// 解析并格式化受限流水号 Pattern，确保预览与真实分配共享同一组确定性规则。
/// Pattern 语法由 {token} 占位符与字面量交替组成：支持 utc:yyyy/utc:yy/utc:MM/utc:dd/utc:HH/utc:mm/utc:ss
/// 七种 UTC 时间段、{tenant} 仅在 Tenant 作用域可用、{sequence:N} 表示 N 位十进制序号宽度（1-18）。
/// 不变量：每个 Pattern 必须包含且仅包含一个 sequence 段；MaximumPatternLength/MaximumOutputLength
/// 双重限制防止正则回溯与超长输出；MaximumSequenceWidth 限制保证 long.MaxValue 容量内可承载。
/// Pattern 一经解析不可变，Format 阶段对 sequenceValue 越界抛 ArgumentOutOfRangeException 以 fail-fast。
/// </summary>
internal sealed class SerialNumberPattern
{
    internal const int MaximumPatternLength = 128;
    internal const int MaximumOutputLength = 128;
    internal const int MaximumTenantIdentifierLength = 64;
    internal const int MaximumSequenceWidth = 18;

    private readonly IReadOnlyList<PatternSegment> segments;

    private SerialNumberPattern(
        IReadOnlyList<PatternSegment> segments,
        int sequenceWidth)
    {
        this.segments = segments;
        SequenceWidth = sequenceWidth;
    }

    /// <summary>序号位宽，由 Pattern 中唯一的 {sequence:N} 段决定。</summary>
    public int SequenceWidth { get; }

    /// <summary>
    /// 当前 Pattern 可生成的最大序号值 = 10^SequenceWidth - 1；
    /// 分配时若计数器达到此值，Allocate* 语句不再自增，上层返回 SequenceExhausted。
    /// </summary>
    public long MaximumSequenceValue
    {
        get
        {
            long value = 1;
            for (var index = 0; index < SequenceWidth; index++)
            {
                value *= 10;
            }

            return value - 1;
        }
    }

    /// <summary>
    /// 解析 Pattern 字符串；返回失败表示 Pattern 为空、超长、未包含 sequence 段、
    /// 包含重复 sequence 段、未闭合的 token 或输出去重。scope 决定 {tenant} 段是否允许。
    /// </summary>
    public static Result<SerialNumberPattern> Parse(
        string? pattern,
        SerialNumberRuleScope scope)
    {
        if (string.IsNullOrEmpty(pattern)
            || pattern.Length > MaximumPatternLength
            || !Enum.IsDefined(scope))
        {
            return Invalid();
        }

        var segments = new List<PatternSegment>();
        var sequenceWidth = 0;
        var maximumOutputLength = 0;
        var literalStart = 0;

        for (var index = 0; index < pattern.Length;)
        {
            if (pattern[index] == '}')
            {
                return Invalid();
            }

            if (pattern[index] != '{')
            {
                index++;
                continue;
            }

            if (index > literalStart)
            {
                var literal = pattern[literalStart..index];
                segments.Add(new PatternSegment(
                    PatternSegmentKind.Literal,
                    literal));
                maximumOutputLength += literal.Length;
            }

            var closingIndex = pattern.IndexOf('}', index + 1);
            if (closingIndex < 0)
            {
                return Invalid();
            }

            var token = pattern[(index + 1)..closingIndex];
            var parsedToken = ParseToken(token, scope);
            if (parsedToken is null)
            {
                return Invalid();
            }

            if (parsedToken.Kind == PatternSegmentKind.Sequence)
            {
                if (sequenceWidth != 0)
                {
                    return Invalid();
                }

                sequenceWidth = parsedToken.Width;
            }

            segments.Add(parsedToken);
            maximumOutputLength += parsedToken.MaximumLength;
            if (maximumOutputLength > MaximumOutputLength)
            {
                return Invalid();
            }

            index = closingIndex + 1;
            literalStart = index;
        }

        if (literalStart < pattern.Length)
        {
            var literal = pattern[literalStart..];
            segments.Add(new PatternSegment(
                PatternSegmentKind.Literal,
                literal));
            maximumOutputLength += literal.Length;
        }

        return sequenceWidth == 0
               || maximumOutputLength > MaximumOutputLength
            ? Invalid()
            : Result<SerialNumberPattern>.Success(
                new SerialNumberPattern(segments, sequenceWidth));
    }

    /// <summary>
    /// 按解析后的段顺序拼装最终流水号；sequenceValue 超出 [0, MaximumSequenceValue] 抛
    /// ArgumentOutOfRangeException，{tenant} 段存在时 tenantIdentifier 不可为空且不超长。
    /// 所有时间字段基于 UTC，禁止使用本地时间以避免跨时区客户端生成不一致流水号。
    /// </summary>
    public string Format(
        DateTimeOffset now,
        string? tenantIdentifier,
        long sequenceValue)
    {
        if (sequenceValue < 0 || sequenceValue > MaximumSequenceValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceValue));
        }

        if (segments.Any(segment =>
                segment.Kind == PatternSegmentKind.Tenant)
            && (string.IsNullOrWhiteSpace(tenantIdentifier)
                || tenantIdentifier.Length > MaximumTenantIdentifierLength))
        {
            throw new ArgumentException(
                "Tenant identifier is required by the serial number pattern.",
                nameof(tenantIdentifier));
        }

        var utc = now.ToUniversalTime();
        var builder = new StringBuilder(MaximumOutputLength);
        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case PatternSegmentKind.Literal:
                    builder.Append(segment.Value);
                    break;
                case PatternSegmentKind.UtcYear:
                    builder.Append(utc.ToString("yyyy", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcYearShort:
                    builder.Append(utc.ToString("yy", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcMonth:
                    builder.Append(utc.ToString("MM", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcDay:
                    builder.Append(utc.ToString("dd", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcHour:
                    builder.Append(utc.ToString("HH", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcMinute:
                    builder.Append(utc.ToString("mm", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.UtcSecond:
                    builder.Append(utc.ToString("ss", CultureInfo.InvariantCulture));
                    break;
                case PatternSegmentKind.Tenant:
                    builder.Append(tenantIdentifier);
                    break;
                case PatternSegmentKind.Sequence:
                    builder.Append(sequenceValue.ToString(
                        $"D{segment.Width}",
                        CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new InvalidOperationException(
                        "The serial number pattern contains an unsupported segment.");
            }
        }

        return builder.ToString();
    }

    private static PatternSegment? ParseToken(
        string token,
        SerialNumberRuleScope scope)
    {
        if (token.StartsWith("sequence:", StringComparison.Ordinal)
            && int.TryParse(
                token.AsSpan("sequence:".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var width)
            && width is >= 1 and <= MaximumSequenceWidth)
        {
            return new PatternSegment(
                PatternSegmentKind.Sequence,
                string.Empty,
                width);
        }

        return token switch
        {
            "utc:yyyy" => new PatternSegment(PatternSegmentKind.UtcYear, string.Empty),
            "utc:yy" => new PatternSegment(PatternSegmentKind.UtcYearShort, string.Empty),
            "utc:MM" => new PatternSegment(PatternSegmentKind.UtcMonth, string.Empty),
            "utc:dd" => new PatternSegment(PatternSegmentKind.UtcDay, string.Empty),
            "utc:HH" => new PatternSegment(PatternSegmentKind.UtcHour, string.Empty),
            "utc:mm" => new PatternSegment(PatternSegmentKind.UtcMinute, string.Empty),
            "utc:ss" => new PatternSegment(PatternSegmentKind.UtcSecond, string.Empty),
            "tenant" when scope == SerialNumberRuleScope.Tenant =>
                new PatternSegment(PatternSegmentKind.Tenant, string.Empty),
            _ => null,
        };
    }

    private static Result<SerialNumberPattern> Invalid() =>
        Result<SerialNumberPattern>.Failure(new Error(
            SerialNumberErrorCodes.PatternInvalid,
            "The serial number pattern is invalid.",
            ErrorType.Validation));

    private enum PatternSegmentKind
    {
        Literal,
        UtcYear,
        UtcYearShort,
        UtcMonth,
        UtcDay,
        UtcHour,
        UtcMinute,
        UtcSecond,
        Tenant,
        Sequence,
    }

    private sealed record PatternSegment(
        PatternSegmentKind Kind,
        string Value,
        int Width = 0)
    {
        public int MaximumLength => Kind switch
        {
            PatternSegmentKind.Literal => Value.Length,
            PatternSegmentKind.UtcYear => 4,
            PatternSegmentKind.UtcYearShort
                or PatternSegmentKind.UtcMonth
                or PatternSegmentKind.UtcDay
                or PatternSegmentKind.UtcHour
                or PatternSegmentKind.UtcMinute
                or PatternSegmentKind.UtcSecond => 2,
            PatternSegmentKind.Tenant => MaximumTenantIdentifierLength,
            PatternSegmentKind.Sequence => Width,
            _ => 0,
        };
    }
}
