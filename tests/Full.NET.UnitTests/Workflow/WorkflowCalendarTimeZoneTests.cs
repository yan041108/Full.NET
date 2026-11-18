using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageWorkCalendars;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>冻结完整 UTC 边界，拒绝损坏、无界和依赖当前系统规则的恢复。</summary>
[TestClass]
public sealed class WorkflowCalendarTimeZoneTests
{
    [TestMethod]
    [DataRow(1000)]
    [DataRow(2026)]
    [DataRow(9998)]
    public void Frozen_custom_zone_restores_without_a_system_registration(int year)
    {
        const string key = "fullnet-frozen-custom-zone";
        Assert.IsFalse(TimeZoneInfo.TryFindSystemTimeZoneById(key, out _));
        var zone = TimeZoneInfo.CreateCustomTimeZone(key, TimeSpan.FromMinutes(345), key, key);
        Assert.IsTrue(WorkflowCalendarTimeZone.TryCapture(zone, out var frozen));
        Assert.IsTrue(WorkflowCalendarTimeZone.TryRead(2, key, frozen!.Rules, out var restored));
        var local = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.IsTrue(restored!.TryToUtc(local, out var instant));
        Assert.AreEqual(local.AddMinutes(-345), instant.UtcDateTime);
        Assert.IsTrue(restored.TryToLocal(instant, out var actual)); Assert.AreEqual(local, actual);
        // 同名规则已变化也不能影响旧发布快照。
        var changed = TimeZoneInfo.CreateCustomTimeZone(key, TimeSpan.FromHours(-3), key, key);
        Assert.AreNotEqual(TimeZoneInfo.ConvertTimeToUtc(local, changed), instant.UtcDateTime);
    }

    [TestMethod]
    public void Half_hour_daylight_transition_preserves_exact_gaps_overlaps_and_boundaries()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 8);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2026, 1, 1), new(2026, 12, 31), TimeSpan.FromMinutes(30), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("fullnet-half-hour-dst", TimeSpan.FromHours(-5), "test", "standard", "daylight", [rule]);
        Assert.IsTrue(WorkflowCalendarTimeZone.TryCapture(zone, out var frozen));
        Assert.IsTrue(WorkflowCalendarTimeZone.TryRead(2, zone.Id, frozen!.Rules, out var restored));
        Assert.IsFalse(restored!.TryToUtc(new(2026, 3, 8, 2, 15, 0), out _));
        Assert.IsFalse(restored.TryToUtc(new(2026, 11, 1, 1, 45, 0), out _));
        Assert.IsTrue(restored.TryToUtc(new(2026, 3, 8, 2, 30, 0), out var instant));
        Assert.AreEqual(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), instant);
        Assert.IsTrue(restored.TryToLocal(instant.AddTicks(-1), out var before));
        Assert.AreEqual(new DateTime(2026, 3, 8, 2, 0, 0).AddTicks(-1), before);
        Assert.IsTrue(restored.TryToLocal(instant, out var after)); Assert.AreEqual(new DateTime(2026, 3, 8, 2, 30, 0), after);
    }

    [TestMethod]
    [DataRow("America/New_York")]
    [DataRow("Europe/Dublin")]
    [DataRow("Australia/Lord_Howe")]
    [DataRow("Pacific/Apia")]
    public void Historical_and_far_future_rules_fit_the_existing_snapshot_budget(string key)
    {
        Assert.IsTrue(WorkflowCalendarTimeZone.TryCapture(TimeZoneInfo.FindSystemTimeZoneById(key), out var frozen));
        Assert.IsTrue(frozen!.Rules.Length <= 65536);
        Assert.IsTrue(WorkflowCalendarTimeZone.TryRead(2, key, frozen.Rules, out var restored));
        foreach (var instant in new[] { new DateTimeOffset(1000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(9998, 12, 31, 23, 59, 59, TimeSpan.Zero) })
        {
            Assert.IsTrue(frozen.TryToLocal(instant, out var expected));
            Assert.IsTrue(restored!.TryToLocal(instant, out var actual)); Assert.AreEqual(expected, actual);
        }
    }

    [TestMethod]
    [DataRow("truncated")]
    [DataRow("trailing")]
    [DataRow("whitespace")]
    [DataRow("oversized")]
    [DataRow("header")]
    [DataRow("count")]
    [DataRow("zero-duration")]
    [DataRow("overflow-duration")]
    [DataRow("incomplete")]
    [DataRow("offset")]
    [DataRow("extra-raw")]
    public void Invalid_or_unbounded_encoded_rules_are_rejected(string fault)
    {
        Assert.IsTrue(WorkflowCalendarTimeZone.TryCapture(TimeZoneInfo.Utc, out var frozen));
        var compressed = Convert.FromBase64String(frozen!.Rules[7..]);
        string rules;
        if (fault == "truncated") rules = "fn-tz1:" + Convert.ToBase64String(compressed[..^1]);
        else if (fault == "trailing") rules = "fn-tz1:" + Convert.ToBase64String([.. compressed, 0]);
        else if (fault == "whitespace") rules = frozen.Rules.Insert(7, " ");
        else
        {
            using var source = new MemoryStream(compressed);
            using var decoder = new BrotliStream(source, CompressionMode.Decompress);
            using var decoded = new MemoryStream(); decoder.CopyTo(decoded);
            var raw = decoded.ToArray();
            switch (fault)
            {
                case "oversized": raw = new byte[240041]; break;
                case "header": raw[0] ^= 1; break;
                case "count": BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(36), int.MaxValue); break;
                case "zero-duration": BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(40), 0); break;
                case "overflow-duration": BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(40), long.MaxValue); break;
                case "incomplete": BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(40), 1); break;
                case "offset": BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(48), int.MinValue); break;
                case "extra-raw": raw = [.. raw, 0]; break;
                default: Assert.Fail("未知测试故障"); break;
            }
            using var target = new MemoryStream();
            using (var encoder = new BrotliStream(target, CompressionLevel.SmallestSize, leaveOpen: true)) encoder.Write(raw);
            rules = "fn-tz1:" + Convert.ToBase64String(target.ToArray());
        }
        Assert.IsFalse(WorkflowCalendarTimeZone.TryRead(2, "UTC", rules, out _));
    }

    [TestMethod]
    public void Rules_are_bound_to_zone_identity_and_snapshot_version()
    {
        Assert.IsTrue(WorkflowCalendarTimeZone.TryCapture(TimeZoneInfo.Utc, out var frozen));
        Assert.IsFalse(WorkflowCalendarTimeZone.TryRead(2, "Asia/Shanghai", frozen!.Rules, out _));
        Assert.IsFalse(WorkflowCalendarTimeZone.TryRead(3, "UTC", frozen.Rules, out _));
        Assert.IsFalse(WorkflowCalendarTimeZone.TryRead(2, "UTC", new string('a', 65537), out _));
    }

    [TestMethod]
    public void Legacy_read_preserves_original_canonical_bytes_and_hash_without_system_fallback()
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1, "UTC", [new(1, [new("09:00", "17:00")])]), out var current));
        var value = JsonNode.Parse(current!.CanonicalJson)!;
        value["schemaVersion"] = 1; value["timeZoneRules"] = TimeZoneInfo.Utc.ToSerializedString();
        using var document = JsonDocument.Parse(value.ToJsonString());
        Assert.IsTrue(WorkflowWorkCalendar.TryRead(document.RootElement, out var legacy));
        using var canonical = JsonDocument.Parse(legacy!.CanonicalJson);
        Assert.IsTrue(WorkflowWorkCalendar.TryRead(canonical.RootElement, out var restored));
        Assert.AreEqual(legacy.CanonicalJson, restored!.CanonicalJson); Assert.AreEqual(legacy.ContentHash, restored.ContentHash);
        value["timeZoneRules"] = "invalid-legacy-rules";
        using var invalid = JsonDocument.Parse(value.ToJsonString()); Assert.IsFalse(WorkflowWorkCalendar.TryRead(invalid.RootElement, out _));
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public async Task Version_response_keeps_editable_draft_schema_and_validates_original_identity(int schema, bool tampered)
    {
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(new(1, "UTC", [new(1, [new("09:00", "17:00")])]), out var calendar));
        if (schema == 1)
        {
            var value = JsonNode.Parse(calendar!.CanonicalJson)!;
            value["schemaVersion"] = 1; value["timeZoneRules"] = TimeZoneInfo.Utc.ToSerializedString();
            using var document = JsonDocument.Parse(value.ToJsonString());
            Assert.IsTrue(WorkflowWorkCalendar.TryRead(document.RootElement, out calendar));
        }
        var row = new WorkflowCalendarVersionRecord(Guid.NewGuid(), Guid.NewGuid(), 1, calendar!.CanonicalJson,
            tampered ? new string('f', 64) : calendar.ContentHash, "UTC", Guid.NewGuid(), DateTimeOffset.UtcNow, "host");
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCalendarVersionRecord>(Arg.Is<SqlStatement>(sql => sql == WorkflowCalendarSql.FindVersion),
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<WorkflowCalendarVersionRecord?>(row));
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var service = new WorkflowWorkCalendarService(query, Substitute.For<ICommandExecutor>(), Substitute.For<ICommandTransaction>(),
            tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Substitute.For<ITenantFeatureEntitlementPort>(), Options.Create(new DatabaseOptions()));
        var result = await service.GetVersionAsync(row.Id, default);
        Assert.AreEqual(!tampered, result.IsSuccess);
        if (tampered) return;
        Assert.IsNotNull(result.Value); Assert.AreEqual(1, result.Value.Calendar.SchemaVersion);
        Assert.AreEqual(row.ContentHash, result.Value.ContentHash);
        Assert.IsTrue(WorkflowWorkCalendar.TryCreate(result.Value.Calendar, out _));
    }
}
