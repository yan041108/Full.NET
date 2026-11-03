using Full.NET.Abstractions.Results;

namespace Full.NET.UnitTests.Results;

[TestClass]
public sealed class TimeIdQueryCursorTests
{
    [TestMethod]
    public void Position_round_trips_exact_utc_ticks_and_identifier()
    {
        var position = new TimeIdQueryPosition(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567), Guid.CreateVersion7());
        var cursor = TimeIdQueryCursor.Encode(position, "imports|tenant|user|queued");
        Assert.AreEqual(76, cursor.Length);
        Assert.IsFalse(cursor.Contains('+') || cursor.Contains('/') || cursor.Contains('='));
        Assert.IsTrue(TimeIdQueryCursor.TryDecode(cursor, "imports|tenant|user|queued", out var result));
        Assert.AreEqual(position.CreatedAtUtc.UtcTicks, result.CreatedAtUtc.UtcTicks);
        Assert.AreEqual(TimeSpan.Zero, result.CreatedAtUtc.Offset);
        Assert.AreEqual(position.Id, result.Id);
    }

    [TestMethod]
    public void Cursor_rejects_other_source_tenant_user_or_filter()
    {
        var cursor = TimeIdQueryCursor.Encode(new(DateTimeOffset.UtcNow, Guid.CreateVersion7()), "imports|a|u|queued");
        foreach (var context in new[] { "reports|a|u|queued", "imports|b|u|queued", "imports|a|v|queued", "imports|a|u|failed" })
            Assert.IsFalse(TimeIdQueryCursor.TryDecode(cursor, context, out _), context);
    }

    [TestMethod]
    public void Cursor_rejects_malformed_unknown_version_invalid_time_and_empty_identifier()
    {
        const string context = "reports|a|u|";
        var cursor = TimeIdQueryCursor.Encode(new(DateTimeOffset.UtcNow, Guid.CreateVersion7()), context);
        foreach (var malformed in new string?[] { null, "", new('a', 10000), new('!', 76), cursor[..^1] })
            Assert.IsFalse(TimeIdQueryCursor.TryDecode(malformed, context, out _));
        var original = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/'));
        foreach (var mutation in new[] { 0, 1, 9 })
        {
            var bytes = original.ToArray();
            if (mutation == 0) bytes[0] = 2;
            else if (mutation == 1) Array.Fill(bytes, (byte)255, 1, 8);
            else Array.Clear(bytes, 9, 16);
            Assert.IsFalse(TimeIdQueryCursor.TryDecode(Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_'), context, out _));
        }
        Assert.ThrowsExactly<ArgumentException>(() => TimeIdQueryCursor.Encode(new(DateTimeOffset.UtcNow, Guid.Empty), context));
    }
}
