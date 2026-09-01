using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 数据库或 Connector 可机器比较的 CDC 位点；用于回退 producer fence 覆盖证明。
/// </summary>
public sealed class CdcDeliveryPosition
{
    /// <summary>
    /// MySQL ROW Binlog 提供商标识。
    /// </summary>
    public const string MySqlProvider = "mysql";

    /// <summary>
    /// SQL Server CDC 提供商标识。
    /// </summary>
    public const string SqlServerProvider = "sqlserver";

    /// <summary>
    /// 提供商标识（MySQL 或 SQL Server）；决定 <see cref="Binlog"/> 或 <see cref="Lsn"/> 哪个字段有效。
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// 位点对应的最后一个事件 ID；用于 Producer Fence 与幂等表交叉校验。
    /// </summary>
    public Guid? LastEventId { get; init; }

    /// <summary>
    /// MySQL Binlog 文件与偏移坐标；Provider 为 <see cref="MySqlProvider"/> 时不为 null。
    /// </summary>
    public MySqlBinlogCoordinates? Binlog { get; init; }

    /// <summary>
    /// SQL Server CDC 提交 LSN 坐标；Provider 为 <see cref="SqlServerProvider"/> 时不为 null。
    /// </summary>
    public SqlServerCdcLsnCoordinates? Lsn { get; init; }

    /// <summary>
    /// 构造 MySQL Binlog 格式的 CDC 位点快照。
    /// </summary>
    /// <param name="lastEventId">该位点前最后处理的集成事件 ID。</param>
    /// <param name="file">Binlog 文件名，如 mysql-bin.000123。</param>
    /// <param name="position">Binlog 文件内的字节偏移。</param>
    public static CdcDeliveryPosition ForMySql(
        Guid? lastEventId,
        string file,
        long position) =>
        new()
        {
            Provider = MySqlProvider,
            LastEventId = lastEventId,
            Binlog = new MySqlBinlogCoordinates(file, position),
        };

    /// <summary>
    /// 构造 SQL Server CDC LSN 字符串格式的位点快照。
    /// </summary>
    /// <param name="lastEventId">该位点前最后处理的集成事件 ID。</param>
    /// <param name="commitLsn">三段式十六进制 LSN 字符串。</param>
    public static CdcDeliveryPosition ForSqlServer(
        Guid? lastEventId,
        string commitLsn) =>
        new()
        {
            Provider = SqlServerProvider,
            LastEventId = lastEventId,
            Lsn = new SqlServerCdcLsnCoordinates(commitLsn),
        };

    /// <summary>
    /// 由 10 字节原始 LSN 字节数组构造 SQL Server CDC 位点快照。
    /// </summary>
    /// <param name="lastEventId">该位点前最后处理的集成事件 ID。</param>
    /// <param name="lsnBytes">长度为 10 的 LSN 原始字节。</param>
    public static CdcDeliveryPosition ForSqlServerBytes(Guid? lastEventId, byte[] lsnBytes) =>
        new()
        {
            Provider = SqlServerProvider,
            LastEventId = lastEventId,
            Lsn = SqlServerCdcLsnCoordinates.FromBytes(lsnBytes),
        };

    /// <summary>
    /// 使用源生成 JSON 上下文序列化为兼容形状的字符串。
    /// </summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, MessagingJsonSerializerContext.Default.CdcDeliveryPosition);

    /// <summary>
    /// 尝试从 JSON 字符串反序列化为有效位点；格式非法或字段缺失时返回 false。
    /// </summary>
    /// <param name="json">来源字符串。</param>
    /// <param name="position">解析成功时输出有效位点快照。</param>
    public static bool TryParse(string? json, out CdcDeliveryPosition? position)
    {
        position = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            position = JsonSerializer.Deserialize(
                json,
                MessagingJsonSerializerContext.Default.CdcDeliveryPosition);
            return position is not null && position.IsValid();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 校验 Provider 与对应坐标字段的一致性；构造持久化快照前必须先通过本校验。
    /// </summary>
    public bool IsValid() =>
        Provider switch
        {
            MySqlProvider => Binlog is not null && Binlog.IsValid(),
            SqlServerProvider => Lsn is not null && Lsn.IsValid(),
            _ => false,
        };

    /// <summary>
    /// 判断 <paramref name="connector"/> 位点是否已覆盖 <paramref name="producerFence"/>。
    /// </summary>
    public static bool ConnectorCoversProducerFence(
        CdcDeliveryPosition producerFence,
        CdcDeliveryPosition connector)
    {
        ArgumentNullException.ThrowIfNull(producerFence);
        ArgumentNullException.ThrowIfNull(connector);
        if (!string.Equals(producerFence.Provider, connector.Provider, StringComparison.Ordinal))
        {
            return false;
        }

        return producerFence.Provider switch
        {
            MySqlProvider => MySqlBinlogCoordinates.Covers(
                producerFence.Binlog!,
                connector.Binlog!),
            SqlServerProvider => SqlServerCdcLsnCoordinates.Covers(
                producerFence.Lsn!,
                connector.Lsn!),
            _ => false,
        };
    }
}

/// <summary>MySQL ROW Binlog 文件与偏移。</summary>
public sealed record MySqlBinlogCoordinates(string File, long Position)
{
    /// <summary>
    /// 校验文件名非空且偏移非负。
    /// </summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(File) && Position >= 0;

    internal static bool Covers(
        MySqlBinlogCoordinates producerFence,
        MySqlBinlogCoordinates connector)
    {
        var fileCompare = CompareBinlogFiles(producerFence.File, connector.File);
        return fileCompare switch
        {
            < 0 => true,
            > 0 => false,
            _ => connector.Position >= producerFence.Position,
        };
    }

    private static int CompareBinlogFiles(string left, string right)
    {
        var leftSuffix = ExtractNumericSuffix(left);
        var rightSuffix = ExtractNumericSuffix(right);
        if (leftSuffix.HasValue && rightSuffix.HasValue)
        {
            var numericCompare = leftSuffix.Value.CompareTo(rightSuffix.Value);
            if (numericCompare != 0)
            {
                return numericCompare;
            }
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static long? ExtractNumericSuffix(string fileName)
    {
        var lastDot = fileName.LastIndexOf('.');
        if (lastDot < 0 || lastDot == fileName.Length - 1)
        {
            return null;
        }

        return long.TryParse(
            fileName[(lastDot + 1)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var suffix)
            ? suffix
            : null;
    }
}

/// <summary>SQL Server CDC commit LSN。</summary>
public sealed record SqlServerCdcLsnCoordinates(string CommitLsn)
{
    /// <summary>
    /// 校验 LSN 字符串是否符合三段式十六进制格式。
    /// </summary>
    public bool IsValid() => TryParseLsn(CommitLsn, out _);

    /// <summary>
    /// 从 10 字节原始二进制 LSN 转换为三段式十六进制字符串表示。
    /// </summary>
    /// <param name="lsnBytes">长度必须为 10 的 LSN 字节数组。</param>
    public static SqlServerCdcLsnCoordinates FromBytes(byte[] lsnBytes)
    {
        ArgumentNullException.ThrowIfNull(lsnBytes);
        if (lsnBytes.Length != 10)
        {
            throw new ArgumentException("SQL Server LSN must be 10 bytes.", nameof(lsnBytes));
        }

        var segmentA = ReadUInt32LittleEndian(lsnBytes, 0);
        var segmentB = ReadUInt32LittleEndian(lsnBytes, 4);
        var segmentC = (ushort)(lsnBytes[8] | (lsnBytes[9] << 8));
        return new SqlServerCdcLsnCoordinates(
            $"{segmentA:x8}:{segmentB:x8}:{segmentC:x4}");
    }

    internal static bool Covers(
        SqlServerCdcLsnCoordinates producerFence,
        SqlServerCdcLsnCoordinates connector) =>
        TryParseLsn(producerFence.CommitLsn, out var fence)
        && TryParseLsn(connector.CommitLsn, out var observed)
        && observed.CompareTo(fence) >= 0;

    private static bool TryParseLsn(string value, out SqlServerLsn parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 3)
        {
            return false;
        }

        if (!uint.TryParse(segments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            || !uint.TryParse(segments[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            || !uint.TryParse(segments[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c))
        {
            return false;
        }

        parsed = new SqlServerLsn(a, b, c);
        return true;
    }

    private static uint ReadUInt32LittleEndian(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24));

    private readonly record struct SqlServerLsn(uint A, uint B, uint C) : IComparable<SqlServerLsn>
    {
        public int CompareTo(SqlServerLsn other)
        {
            var compare = A.CompareTo(other.A);
            if (compare != 0)
            {
                return compare;
            }

            compare = B.CompareTo(other.B);
            return compare != 0 ? compare : C.CompareTo(other.C);
        }
    }
}
