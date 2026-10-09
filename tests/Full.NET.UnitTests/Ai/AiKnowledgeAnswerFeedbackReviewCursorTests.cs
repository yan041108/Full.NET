using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Domain;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeAnswerFeedbackReviewCursorTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Cursor_roundtrips_without_disclosing_position(bool scoped)
    {
        var f = Create(); var actor = Guid.CreateVersion7(); var position = Guid.CreateVersion7();
        Guid? tenant = scoped ? Guid.CreateVersion7() : null;
        var cursor = f.Cursor.Protect(position, actor, tenant);
        Assert.AreEqual(position, f.Cursor.Read(cursor, actor, tenant));
        Assert.IsFalse(cursor.Contains(position.ToString("N"), StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(f.Cursor.Read(cursor, Guid.CreateVersion7(), tenant));
        Assert.IsNull(f.Cursor.Read(cursor, actor, scoped ? null : Guid.CreateVersion7()));
    }

    [TestMethod]
    public void Expired_tampered_and_unbounded_cursors_fail_closed()
    {
        var f = Create(); var actor = Guid.CreateVersion7(); var cursor = f.Cursor.Protect(Guid.CreateVersion7(), actor, null);
        Assert.IsNull(f.Cursor.Read(cursor + "bad", actor, null));
        Assert.IsNull(f.Cursor.Read(new string('a', 2049), actor, null));
        Assert.IsNull(f.Cursor.Read("", actor, null));
        f.Clock.UtcNow.Returns(f.Clock.UtcNow.AddMinutes(16));
        Assert.IsNull(f.Cursor.Read(cursor, actor, null));
    }

    private static (AiKnowledgeAnswerFeedbackReviewCursor Cursor, IClock Clock) Create()
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return (new(new EphemeralDataProtectionProvider(), clock), clock);
    }
}
