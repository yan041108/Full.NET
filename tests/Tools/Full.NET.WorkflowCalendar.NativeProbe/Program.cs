using Full.NET.Modules.Workflow.Domain;

// 原生外部进程执行实际 BCL 适配器、压缩恢复和边界映射，不使用测试替身。
var cases = new[]
{
    ("America/New_York", new DateTime(2026, 3, 8, 1, 30, 0), (DateTime?)new DateTime(2026, 3, 8, 6, 30, 0)),
    ("America/New_York", new DateTime(2026, 3, 8, 2, 30, 0), null),
    ("America/New_York", new DateTime(2026, 3, 8, 3, 30, 0), new DateTime(2026, 3, 8, 7, 30, 0)),
    ("America/New_York", new DateTime(2026, 11, 1, 1, 30, 0), null),
    ("America/New_York", new DateTime(2026, 11, 1, 2, 30, 0), new DateTime(2026, 11, 1, 7, 30, 0)),
    ("Asia/Shanghai", new DateTime(2026, 10, 26, 9, 0, 0), new DateTime(2026, 10, 26, 1, 0, 0)),
};
foreach (var (key, local, expected) in cases)
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById(key);
    Require(WorkflowCalendarTimeZone.TryCapture(zone, out var captured), "发布冻结");
    Require(WorkflowCalendarTimeZone.TryRead(2, key, captured!.Rules, out var restored), "解压恢复");
    var valid = restored!.TryToUtc(local, out var instant);
    Require(valid == expected.HasValue, "跳空/歧义拒绝");
    if (expected is { } utc)
    {
        Require(instant.UtcDateTime == utc, "UTC 交集边界");
        Require(restored.TryToLocal(instant, out var actual) && actual == local, "本地反向映射");
    }
}
const string customKey = "fullnet-native-frozen-only";
Require(!TimeZoneInfo.TryFindSystemTimeZoneById(customKey, out _), "不存在的系统注册");
var custom = TimeZoneInfo.CreateCustomTimeZone(customKey, TimeSpan.FromMinutes(345), customKey, customKey);
Require(WorkflowCalendarTimeZone.TryCapture(custom, out var customSnapshot), "自定义规则冻结");
Require(WorkflowCalendarTimeZone.TryRead(2, customKey, customSnapshot!.Rules, out var frozenCustom), "无系统查询恢复");
Require(frozenCustom!.TryToUtc(new(2026, 1, 1, 9, 0, 0), out var customInstant) &&
    customInstant.UtcDateTime == new DateTime(2026, 1, 1, 3, 15, 0), "冻结的非整点偏移");
Require(!WorkflowCalendarTimeZone.TryRead(2, "UTC", customSnapshot.Rules, out _), "身份绑定拒绝");
Require(WorkflowCalendarTimeZone.TryRead(1, "UTC", TimeZoneInfo.Utc.ToSerializedString(), out var legacy), "旧版本读取");
Require(legacy!.TryToUtc(new(2026, 1, 1, 9, 0, 0), out var legacyInstant) &&
    legacyInstant.UtcDateTime == new DateTime(2026, 1, 1, 9, 0, 0), "旧规则映射");
Console.WriteLine($"Native calendar probe passed: {cases.Length} transition cases, custom frozen zone, identity rejection and legacy rules.");
return 0;

static void Require(bool valid, string stage)
{
    if (!valid) throw new InvalidOperationException($"原生工作日历探针失败：{stage}。");
}
