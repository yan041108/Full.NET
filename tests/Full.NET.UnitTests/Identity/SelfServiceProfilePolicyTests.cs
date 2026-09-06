using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.SelfServiceProfile;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class SelfServiceProfilePolicyTests
{
    [TestMethod]
    public void GetWritableProfileFieldKeys_excludes_sensitive_and_internal_fields()
    {
        var writable = SelfServiceProfilePolicy.GetWritableProfileFieldKeys(
        [
            "nickname",
            "phone_number",
            "email",
            "employee_number",
            "gender",
            "join_date_utc",
            "sort_order",
            "id_card_number",
            "address",
        ]);

        CollectionAssert.AreEquivalent(
            new[] { "nickname", "email", "gender", "address" },
            writable.ToArray());
    }

    [TestMethod]
    public void ValidateNoReadOnlyFieldsInPatch_rejects_phone_number()
    {
        var error = SelfServiceProfilePolicy.ValidateNoReadOnlyFieldsInPatch(
            ["nickname", "phone_number"]);

        Assert.IsNotNull(error);
        Assert.AreEqual(
            IdentityErrorCodes.SelfServiceProfileReadOnlyFieldRejected,
            error!.Code);
        Assert.AreEqual(ErrorType.Validation, error.Type);
    }

    [TestMethod]
    public void IsHostActorScope_matches_host_only()
    {
        Assert.IsTrue(SelfServiceProfilePolicy.IsHostActorScope("host"));
        Assert.IsFalse(SelfServiceProfilePolicy.IsHostActorScope("tenant"));
    }
}
