using System.Globalization;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers.Domain;

/// <summary>
/// 流水号计数器重置周期桶生成器。按 ResetInterval 将当前 UTC 时间折叠为稳定字符串，
/// 作为 (RuleId, TenantId, ResetBucket) 计数器行的物理隔离键；
/// Never 始终返回 "all" 表示永不重置，Day/Month/Year 分别按 yyyyMMdd/yyyyMM/yyyy 折叠。
/// 跨重置周期切换时，由于 ResetBucket 字符串不同，会触发 Allocate* 语句的"计数器不存在"分支
/// 并 INSERT 新行从 MinimumValue 重新开始，无需显式归零旧计数器。
/// </summary>
internal static class SerialNumberResetBucket
{
    /// <summary>
    /// 根据重置周期生成当前周期桶字符串；输入必须为已枚举定义的 ResetInterval，
    /// 未定义值抛出 ArgumentOutOfRangeException 以 fail-fast 而非静默返回错误桶。
    /// </summary>
    public static string Create(
        SerialNumberResetInterval interval,
        DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return interval switch
        {
            SerialNumberResetInterval.Never => "all",
            SerialNumberResetInterval.Day =>
                utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            SerialNumberResetInterval.Month =>
                utc.ToString("yyyyMM", CultureInfo.InvariantCulture),
            SerialNumberResetInterval.Year =>
                utc.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(interval)),
        };
    }
}
