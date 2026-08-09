using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 数据库或 Connector 可机器比较的 CDC 位点；用于回退 producer fence 覆盖证明。
/// </summary>
public sealed class CdcDeliveryPosition
{
    public const string MySqlProvider = "mysql";
    public const string SqlServerProvider = "sqlserver";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Provider { get; init; } = string.Empty;

    public Guid? LastEventId { get; init; }

    public MySqlBinlogCoordinates? Binlog { get; init; }

    public SqlServerCdcLsnCoordinates? Lsn { get; init; }

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

    public static CdcDeliveryPosition ForSqlServer(
        Guid? lastEventId,
        string commitLsn) =>
        new()
        {
            Provider = SqlServerProvider,
            LastEventId = lastEventId,
            Lsn = new SqlServerCdcLsnCoordinates(commitLsn),
        };

    public static CdcDeliveryPosition ForSqlServerBytes(Guid? lastEventId, byte[] lsnBytes) =>
        new()
        {
            Provider = SqlServerProvider,
            LastEventId = lastEventId,
            Lsn = SqlServerCdcLsnCoordinates.FromBytes(lsnBytes),
        };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string? json, out CdcDeliveryPosition? position)
    {
        position = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            position = JsonSerializer.Deserialize<CdcDeliveryPosition>(json, JsonOptions);
            return position is not null && position.IsValid();
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
    public bool IsValid() => TryParseLsn(CommitLsn, out _);

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
