using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOidcClients;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OidcClientManagementServiceTests
{
    [TestMethod]
    public void ValidateCreateRequest_rejects_invalid_client_id_and_redirect_uri()
    {
        var blankClientId = OidcClientManagementService.ValidateCreateRequest(
            new CreateOidcClientRequest(
                "   ",
                "名称",
                ["https://localhost/callback"],
                [],
                ["openid"],
                false,
                true,
                null));
        Assert.IsFalse(blankClientId.IsSuccess);

        var invalidRedirect = OidcClientManagementService.ValidateCreateRequest(
            new CreateOidcClientRequest(
                "valid-client",
                "名称",
                ["not-a-uri"],
                [],
                ["openid"],
                false,
                true,
                null));
        Assert.IsFalse(invalidRedirect.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, invalidRedirect.Error!.Code);
    }

    [TestMethod]
    public void ValidateCreateRequest_requires_openid_scope()
    {
        var result = OidcClientManagementService.ValidateCreateRequest(
            new CreateOidcClientRequest(
                "valid-client",
                "名称",
                ["https://localhost/callback"],
                [],
                ["profile"],
                false,
                true,
                null));
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ValidateUpdateRequest_normalizes_display_name()
    {
        var result = OidcClientManagementService.ValidateUpdateRequest(
            new UpdateOidcClientRequest(
                " 展示名称 ",
                ["https://localhost/callback"],
                [],
                ["openid", "profile"],
                true,
                null,
                1));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("展示名称", result.Value!.DisplayName);
    }
}