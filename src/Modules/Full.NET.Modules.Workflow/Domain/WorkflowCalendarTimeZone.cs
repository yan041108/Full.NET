using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NodaTime;
using NodaTime.TimeZones;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布时展开规则，恢复时只读取有界快照，不重新解析操作系统时区。</summary>
internal sealed class WorkflowCalendarTimeZone
{
    private const string Prefix = "fn-tz1:";
    private const int HeaderLength = 40;
    private const int MaximumIntervals = 20000;
    private const int MaximumRawLength = HeaderLength + MaximumIntervals * 12;
    private const int MaximumCompressedLength = 49146;
    private const int MaximumOffsetSeconds = 14 * 3600;
    // 支持的本地日期为 1000—9998；两侧留出偏移和 24:00 转换的完整余量。
    private static readonly long LowerTicks = new DateTime(999, 12, 30, 0, 0, 0, DateTimeKind.Utc).Ticks;
    private static readonly long UpperTicks = new DateTime(9999, 1, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
    private readonly ZoneInterval[] intervals;
    private readonly int[] offsets;
    private readonly TimeZoneInfo? legacy;

    private WorkflowCalendarTimeZone(string id, string rules, ZoneInterval[] intervals)
    {
        Id = id; Rules = rules; SchemaVersion = 2; this.intervals = intervals;
        offsets = intervals.Select(interval => interval.OffsetSeconds).Distinct().ToArray();
    }

    private WorkflowCalendarTimeZone(TimeZoneInfo zone, string rules)
    { Id = zone.Id; Rules = rules; SchemaVersion = 1; legacy = zone; intervals = []; offsets = []; }

    internal string Id { get; }
    internal string Rules { get; }
    internal int SchemaVersion { get; }

    internal static bool TryCapture(TimeZoneInfo zone, out WorkflowCalendarTimeZone? snapshot)
    {
        snapshot = null;
        // Noda Time 负责 Windows/Unix 调整规则和年界拼接，不自行重写 DST 规则引擎。
        // 其午夜规范化语义属于新快照版本，旧版本仍按原 TimeZoneInfo 规则解释。
        var source = BclDateTimeZone.FromTimeZoneInfo(zone);
        var lower = Instant.FromDateTimeUtc(new DateTime(LowerTicks, DateTimeKind.Utc));
        var upper = Instant.FromDateTimeUtc(new DateTime(UpperTicks, DateTimeKind.Utc));
        var values = new List<ZoneInterval>();
        foreach (var interval in source.GetZoneIntervals(lower, upper))
        {
            var end = !interval.HasEnd || interval.End >= upper ? UpperTicks : interval.End.ToDateTimeUtc().Ticks;
            var offset = interval.WallOffset.Seconds;
            if (Math.Abs((long)offset) > MaximumOffsetSeconds) return false;
            if (values.Count > 0 && values[^1].OffsetSeconds == offset) values[^1] = new(end, offset);
            else values.Add(new(end, offset));
            if (values.Count > MaximumIntervals) return false;
        }
        if (values.Count == 0 || values[^1].EndTicks != UpperTicks || values.Select(value => value.OffsetSeconds).Distinct().Count() > 64) return false;
        var raw = new byte[HeaderLength + values.Count * 12];
        "FNZ1"u8.CopyTo(raw);
        SHA256.HashData(Encoding.UTF8.GetBytes(zone.Id)).CopyTo(raw, 4);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(36), values.Count);
        var previous = LowerTicks;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value.EndTicks <= previous) return false;
            var entry = raw.AsSpan(HeaderLength + index * 12, 12);
            // 保存相邻 UTC 边界的距离，使数千年的周期规则仍处于既有载荷预算内。
            BinaryPrimitives.WriteInt64LittleEndian(entry, value.EndTicks - previous);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], value.OffsetSeconds);
            previous = value.EndTicks;
        }
        var compressed = new byte[MaximumCompressedLength];
        if (!BrotliEncoder.TryCompress(raw, compressed, out var written, quality: 11, window: 22)) return false;
        snapshot = new(zone.Id, Prefix + Convert.ToBase64String(compressed, 0, written), values.ToArray());
        return true;
    }

    internal static bool TryRead(int schemaVersion, string id, string rules, out WorkflowCalendarTimeZone? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrEmpty(rules) || rules.Length > 65536) return false;
        if (schemaVersion == 1)
        {
            // 已发布的旧版本不得重读当前规则或重算摘要；无法解析时失败关闭。
            var zone = TimeZoneInfo.FromSerializedString(rules);
            if (zone.Id != id) return false;
            snapshot = new(zone, rules); return true;
        }
        if (schemaVersion != 2 || !rules.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var compressed = new byte[MaximumCompressedLength];
        if (!Convert.TryFromBase64String(rules[Prefix.Length..], compressed, out var count) ||
            rules[Prefix.Length..] != Convert.ToBase64String(compressed, 0, count)) return false;
        var raw = new byte[MaximumRawLength];
        using var decoder = new BrotliDecoder();
        if (decoder.Decompress(compressed.AsSpan(0, count), raw, out var consumed, out var written) != OperationStatus.Done ||
            consumed != count || written < HeaderLength || !raw.AsSpan(0, 4).SequenceEqual("FNZ1"u8) ||
            !raw.AsSpan(4, 32).SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(id)))) return false;
        var length = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(36));
        if (length is < 1 or > MaximumIntervals || written != HeaderLength + length * 12) return false;
        var values = new ZoneInterval[length]; var previous = LowerTicks; var offsets = new HashSet<int>();
        for (var index = 0; index < length; index++)
        {
            var entry = raw.AsSpan(HeaderLength + index * 12, 12);
            var duration = BinaryPrimitives.ReadInt64LittleEndian(entry);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            if (duration <= 0 || duration > UpperTicks - previous || Math.Abs((long)offset) > MaximumOffsetSeconds ||
                (index > 0 && values[index - 1].OffsetSeconds == offset)) return false;
            previous += duration; values[index] = new(previous, offset); offsets.Add(offset);
            if (offsets.Count > 64) return false;
        }
        if (previous != UpperTicks) return false;
        // 原始编码参与版本摘要；读取时不重新压缩，避免运行库升级改变固定身份。
        snapshot = new(id, rules, values); return true;
    }

    internal bool TryToLocal(DateTimeOffset instant, out DateTime local)
    {
        local = default;
        if (legacy is not null) { local = TimeZoneInfo.ConvertTime(instant, legacy).DateTime; return true; }
        if (!TryOffset(instant.UtcTicks, out var offset)) return false;
        local = new DateTime(instant.UtcTicks + offset * TimeSpan.TicksPerSecond, DateTimeKind.Unspecified); return true;
    }

    internal bool TryToUtc(DateTime local, out DateTimeOffset instant)
    {
        instant = default;
        if (legacy is not null)
        {
            if (legacy.IsInvalidTime(local) || legacy.IsAmbiguousTime(local)) return false;
            instant = new(TimeZoneInfo.ConvertTimeToUtc(local, legacy)); return true;
        }
        var matches = 0;
        foreach (var offset in offsets)
        {
            var ticks = local.Ticks - offset * TimeSpan.TicksPerSecond;
            if (!TryOffset(ticks, out var actual) || actual != offset) continue;
            // 跳空没有候选，回拨有两个候选；二者均不得被隐式归一化。
            if (++matches > 1) return false;
            instant = new(ticks, TimeSpan.Zero);
        }
        return matches == 1;
    }

    private bool TryOffset(long ticks, out int offset)
    {
        offset = 0; if (ticks < LowerTicks || ticks >= UpperTicks) return false;
        var left = 0; var right = intervals.Length - 1;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (ticks < intervals[middle].EndTicks) right = middle; else left = middle + 1;
        }
        offset = intervals[left].OffsetSeconds; return true;
    }

    private readonly record struct ZoneInterval(long EndTicks, int OffsetSeconds);
}
