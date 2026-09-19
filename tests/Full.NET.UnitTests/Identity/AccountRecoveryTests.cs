using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountRecoveryTests
{
    [TestMethod]
    public void Account_challenge_credential_hash_is_stable_for_same_inputs()
    {
        var challengeId = Guid.Parse("018f5f40-0000-7000-8000-000000000001");
        var first = AccountChallengeCredentialHasher.Hash(challengeId, "123456");
        var second = AccountChallengeCredentialHasher.Hash(challengeId, "123456");
        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first.Length);
    }

    [TestMethod]
    public void Registration_invitation_credential_hash_changes_with_token()
    {
        var invitationId = Guid.Parse("018f5f40-0000-7000-8000-000000000002");
        var first = RegistrationInvitationCredentialHasher.Hash(invitationId, "token-a");
        var second = RegistrationInvitationCredentialHasher.Hash(invitationId, "token-b");
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Registration_policy_map_keeps_open_mode_from_legacy_boolean()
    {
        var record = new RegistrationPolicyRecord(
            IdentityRegistrationPolicyConstants.PolicyId,
            true,
            0,
            DateTimeOffset.UtcNow,
            1);
        var response = RegistrationPolicyService.Map(record);
        Assert.AreEqual(IdentityRegistrationMode.Open, response.RegistrationMode);
        Assert.IsTrue(response.IsPublicRegistrationEnabled);
    }

    [TestMethod]
    public void Registration_policy_resolve_prefers_explicit_mode()
    {
        var mode = RegistrationPolicyService.ResolveRegistrationMode(
            new UpdateRegistrationPolicyRequest(false, 1, IdentityRegistrationMode.InvitationOnly));
        Assert.AreEqual(IdentityRegistrationMode.InvitationOnly, mode);
    }

    [TestMethod]
    public void Normalize_email_rejects_invalid_values()
    {
        Assert.IsNull(AccountChallengeService.NormalizeEmail(" "));
        Assert.IsNull(AccountChallengeService.NormalizeEmail("not-an-email"));
        Assert.AreEqual("user@example.com", AccountChallengeService.NormalizeEmail(" User@Example.com "));
    }
}
