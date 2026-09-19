using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class TenantMembershipTests
{
    [TestMethod]
    public void ValidateInvitationRequest_rejects_invalid_email_and_role()
    {
        var invalidEmail = TenantMembershipManagementService.ValidateInvitationRequest(
            new CreateTenantInvitationRequest("bad-email", TenantMemberRoles.Member));
        Assert.IsFalse(invalidEmail.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, invalidEmail.Error!.Code);

        var invalidRole = TenantMembershipManagementService.ValidateInvitationRequest(
            new CreateTenantInvitationRequest("user@example.com", "SuperUser"));
        Assert.IsFalse(invalidRole.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.TenantMemberRoleInvalid, invalidRole.Error!.Code);
    }

    [TestMethod]
    public void ValidateInvitationRequest_normalizes_email()
    {
        var result = TenantMembershipManagementService.ValidateInvitationRequest(
            new CreateTenantInvitationRequest(" User@Example.COM ", TenantMemberRoles.Admin));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("user@example.com", result.Value.Email);
        Assert.AreEqual(TenantMemberRoles.Admin, result.Value.Role);
    }

    [TestMethod]
    public void ValidateInvitationRequest_rejects_owner_role()
    {
        var result = TenantMembershipManagementService.ValidateInvitationRequest(
            new CreateTenantInvitationRequest("owner@example.com", TenantMemberRoles.Owner));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.TenantMemberRoleInvalid, result.Error!.Code);
    }
}
