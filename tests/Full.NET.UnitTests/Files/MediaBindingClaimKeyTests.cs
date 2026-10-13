using Full.NET.Modules.Files.Contracts;

namespace Full.NET.UnitTests.Files;

/// <summary>解绑只针对已退役代际，重新绑定同一文件必须拥有另一条删除保护。</summary>
[TestClass]
public sealed class MediaBindingClaimKeyTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", "")]
    [DataRow(" ", " ")]
    [DataRow("identity-user-avatar:valid", null)]
    public void Missing_keys_cannot_authorize_a_release(string? legacy, string? candidate) =>
        Assert.IsFalse(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy!, candidate!));

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public void Rebinding_same_subject_and_file_uses_an_independent_claim(string kind)
    {
        var subject = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
        var binding = Guid.CreateVersion7();
        var first = Key(kind, subject, file, binding);
        Assert.AreEqual(first, Key(kind, subject, file, binding));
        Assert.AreNotEqual(first, Key(kind, subject, file, Guid.CreateVersion7()));
        Assert.AreNotEqual(Legacy(kind, subject, file), first);
        Assert.IsTrue(first.Length <= 200);
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public void Claim_validation_accepts_legacy_and_current_generation_but_rejects_other_subjects(string kind)
    {
        var subject = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var legacy = Legacy(kind, subject, file);
        Assert.IsTrue(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, legacy));
        Assert.IsTrue(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, Key(kind, subject, file, Guid.CreateVersion7())));
        Assert.IsFalse(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, Key(kind, Guid.CreateVersion7(), file, Guid.CreateVersion7())));
        Assert.IsFalse(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, Key(kind, subject, Guid.CreateVersion7(), Guid.CreateVersion7())));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    [DataRow("not-a-guid")]
    [DataRow("00000000000070008000000000000001")]
    public void Invalid_generation_cannot_authorize_a_release(string suffix)
    {
        var legacy = Legacy("avatar", Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.IsFalse(HostFileReferenceClaimIdempotencyKeys.IsBindingKey(legacy, legacy + ":binding:" + suffix));
    }

    [TestMethod]
    [DataRow("avatar")]
    [DataRow("signature")]
    [DataRow("logo")]
    public void Empty_generation_is_rejected_before_claim_creation(string kind) =>
        Assert.ThrowsExactly<ArgumentException>(() => Key(kind, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty));

    private static string Legacy(string kind, Guid subject, Guid file) => kind switch
    {
        "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file),
        "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file),
    };
    private static string Key(string kind, Guid subject, Guid file, Guid binding) => kind switch
    {
        "avatar" => HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(subject, file, binding),
        "signature" => HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(subject, file, binding),
        _ => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(subject, file, binding),
    };
}
