using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Abstractions.Results;

/// <summary>按时间及标识稳定排序的分页位置；不携带授权事实。</summary>
/// <param name="CreatedAtUtc">数据库实际返回的创建 UTC 时间。</param>
/// <param name="Id">同一时间内的稳定标识。</param>
public readonly record struct TimeIdQueryPosition(DateTimeOffset CreatedAtUtc, Guid Id);

/// <summary>供导入和报表摘要复用的有界分页编码；上下文只防止误用，授权仍取可信用户及租户。</summary>
public static class TimeIdQueryCursor
{
    private const int PayloadLength = 57;

    /// <summary>编码固定版本位置并绑定来源、租户、用户和筛选上下文。</summary>
    /// <param name="position">数据库返回的位置，不使用客户端时间重新构造。</param>
    /// <param name="context">由服务端构造的规范查询上下文。</param>
    public static string Encode(TimeIdQueryPosition position, string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        if (position.Id == Guid.Empty) throw new ArgumentException("分页标识不能为空。", nameof(position));
        Span<byte> payload = stackalloc byte[PayloadLength]; payload[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(payload[1..9], position.CreatedAtUtc.UtcTicks);
        position.Id.TryWriteBytes(payload[9..25]);
        SHA256.HashData(Encoding.UTF8.GetBytes(context), payload[25..]);
        return Convert.ToBase64String(payload).Replace('+', '-').Replace('/', '_');
    }

    /// <summary>拒绝过长、损坏、版本不符或上下文不匹配的位置；调用方仍须独立绑定权限和 SQL 租户。</summary>
    /// <param name="cursor">客户端返回的分页位置。</param>
    /// <param name="context">本次可信查询上下文。</param>
    /// <param name="position">验证成功后的排序位置。</param>
    public static bool TryDecode(string? cursor, string context, out TimeIdQueryPosition position)
    {
        position = default;
        if (cursor?.Length != 76 || string.IsNullOrWhiteSpace(context)) return false;
        Span<byte> payload = stackalloc byte[PayloadLength];
        if (!Convert.TryFromBase64String(cursor.Replace('-', '+').Replace('_', '/'), payload, out var count)
            || count != PayloadLength || payload[0] != 1) return false;
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(Encoding.UTF8.GetBytes(context), hash);
        if (!CryptographicOperations.FixedTimeEquals(payload[25..], hash)) return false;
        var ticks = BinaryPrimitives.ReadInt64BigEndian(payload[1..9]); var id = new Guid(payload[9..25]);
        if (ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks || id == Guid.Empty) return false;
        position = new(new DateTimeOffset(ticks, TimeSpan.Zero), id); return true;
    }
}
