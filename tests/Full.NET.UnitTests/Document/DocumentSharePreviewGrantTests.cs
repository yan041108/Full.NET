using Full.NET.Abstractions.Time;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>短期凭据隔离、撤销和访问计数变化回归。</summary>
[TestClass]
public sealed class DocumentSharePreviewGrantTests
{
    [TestMethod]
    public void Grants_bind_share_document_task_purpose_and_expiration()
    {
        var clock = Substitute.For<IClock>(); var now = DateTimeOffset.UtcNow; clock.UtcNow.Returns(now);
        var options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions());
        var grants = new DocumentSharePreviewGrantService(new EphemeralDataProtectionProvider(), clock, options);
        var share = Share(); var task = Guid.CreateVersion7();
        var create = grants.Issue(share, Guid.Empty); var output = grants.Issue(share, task, create.ExpiresAtUtc);
        Assert.IsTrue(grants.IsValid(create.Token, share, Guid.Empty));
        Assert.IsFalse(grants.IsValid(create.Token, share, task));
        Assert.IsTrue(grants.IsValid(output.Token, share, task));
        Assert.IsFalse(grants.IsValid(output.Token, share, Guid.Empty));
        Assert.IsFalse(grants.IsValid(output.Token, share, Guid.CreateVersion7()));
        Assert.IsFalse(grants.IsValid(output.Token, Share(), task));
        Assert.IsFalse(grants.IsValid(output.Token + "x", share, task));
        Assert.AreEqual(grants.GetCreationOperation(create.Token, share), grants.GetCreationOperation(create.Token, share));
        clock.UtcNow.Returns(create.ExpiresAtUtc.AddSeconds(1));
        Assert.IsFalse(grants.IsValid(create.Token, share, Guid.Empty));
        Assert.IsFalse(grants.IsValid(output.Token, share, task));
    }
    [TestMethod]
    public void Other_consumption_keeps_grant_but_policy_change_revokes_it()
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions());
        var grants = new DocumentSharePreviewGrantService(new EphemeralDataProtectionProvider(), clock, options);
        var original = Share(); var token = grants.Issue(original, Guid.Empty).Token;
        var consumed = Copy(original, 2, 3);
        Assert.IsTrue(grants.IsValid(token, consumed, Guid.Empty));
        Assert.IsFalse(grants.IsValid(token, Copy(original, 1, 3), Guid.Empty));
        Assert.IsNull(grants.GetCreationOperation("malformed", original));
    }
    private static DocumentShareRecord Share() => new() { Id=Guid.CreateVersion7(), DocumentId=Guid.CreateVersion7(),
        ExpireTime=DateTimeOffset.UtcNow.AddHours(1), AccessCount=1, Version=2, IsEnabled=true, PasswordHash="hash" };
    private static DocumentShareRecord Copy(DocumentShareRecord source, int access, long version) => new() {
        Id=source.Id, DocumentId=source.DocumentId, ExpireTime=source.ExpireTime, PasswordHash=source.PasswordHash,
        AccessCount=access, Version=version, IsEnabled=true };
}
