using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;

/// <summary>生成游标摘要时使用的规范化访问日志筛选。</summary>
internal sealed record AccessLogCursorFilter(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? HttpMethod,
    int? StatusCode,
    string? PathContains);

/// <summary>访问日志稳定排序中的最后一条已返回记录。</summary>
internal readonly record struct AccessLogCursorBoundary(
    DateTimeOffset OccurredAtUtc,
    Guid Id);

/// <summary>
/// 编解码不透明访问日志游标，并绑定产生游标时的规范化筛选。
/// 游标不承担授权或防篡改职责，Host 数据范围仍由查询执行边界保证。
/// </summary>
internal static class AccessLogCursorCodec
{
    private const byte CurrentVersion = 1;
    private const int TimestampLength = sizeof(long);
    private const int IdentifierLength = 16;
    private const int FilterDigestLength = 32;
    private const int PayloadLength =
        sizeof(byte) + TimestampLength + IdentifierLength + FilterDigestLength;
    private const int EncodedLength = ((PayloadLength * 4) + 2) / 3;

    public static string Encode(
        AccessLogCursorBoundary boundary,
        AccessLogCursorFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (boundary.Id == Guid.Empty)
        {
            throw new ArgumentException("访问日志游标边界 ID 不能为空。", nameof(boundary));
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = CurrentVersion;
        BinaryPrimitives.WriteInt64BigEndian(
            payload.Slice(1, TimestampLength),
            boundary.OccurredAtUtc.UtcTicks);
        boundary.Id.TryWriteBytes(
            payload.Slice(1 + TimestampLength, IdentifierLength),
            bigEndian: true,
            out _);
        ComputeFilterDigest(filter).CopyTo(
            payload.Slice(1 + TimestampLength + IdentifierLength));

        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        AccessLogCursorFilter filter,
        out AccessLogCursorBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(filter);
        boundary = default;
        if (string.IsNullOrWhiteSpace(cursor)
            || cursor.Length != EncodedLength
            || cursor.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_')))
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            var payload = Convert.FromBase64String(padded);
            if (payload.Length != PayloadLength || payload[0] != CurrentVersion)
            {
                return false;
            }

            var expectedDigest = ComputeFilterDigest(filter);
            var actualDigest = payload.AsSpan(
                1 + TimestampLength + IdentifierLength,
                FilterDigestLength);
            if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            {
                return false;
            }

            var utcTicks = BinaryPrimitives.ReadInt64BigEndian(
                payload.AsSpan(1, TimestampLength));
            var id = new Guid(
                payload.AsSpan(1 + TimestampLength, IdentifierLength),
                bigEndian: true);
            if (id == Guid.Empty)
            {
                return false;
            }

            boundary = new AccessLogCursorBoundary(
                new DateTimeOffset(utcTicks, TimeSpan.Zero),
                id);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException
            or ArgumentException
            or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static byte[] ComputeFilterDigest(AccessLogCursorFilter filter)
    {
        var canonical = string.Join(
            "|",
            FormatTimestamp(filter.FromUtc),
            FormatTimestamp(filter.ToUtc),
            EncodeText(filter.HttpMethod),
            filter.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
            EncodeText(filter.PathContains));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-";

    // 文本先转 Base64，避免分隔符出现在筛选值中导致不同筛选产生相同规范串。
    private static string EncodeText(string? value) =>
        value is null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
