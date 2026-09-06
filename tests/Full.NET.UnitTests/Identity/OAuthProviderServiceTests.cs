using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageOAuthProviders;
using Full.NET.Modules.Identity.OAuth;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OAuthProviderManagementServiceTests
{
    [TestMethod]
    public void ValidateMetadata_rejects_invalid_provider_key()
    {
        var result = OAuthProviderManagementService.ValidateMetadata(
            "Invalid_Key",
            "Example",
            "https://login.example.com",
            "client-id",
            null,
            null);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.OAuthProviderInvalidKey, result.Error!.Code);
    }

    [TestMethod]
    public void ValidateMetadata_applies_default_scopes_and_redirect_path()
    {
        var result = OAuthProviderManagementService.ValidateMetadata(
            "example-idp",
            "Example IdP",
            "https://login.example.com/",
            "client-id",
            null,
            null);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            OAuthProviderManagementService.DefaultScopes,
            result.Value!.Scopes);
        Assert.AreEqual(
            OAuthProviderManagementService.DefaultRedirectPath,
            result.Value.RedirectPath);
    }
}

[TestClass]
public sealed class OAuthPkceTests
{
    [TestMethod]
    public void CreateCodeChallenge_is_deterministic_for_verifier()
    {
        const string verifier = "test-verifier-value";
        var challenge1 = OAuthPkce.CreateCodeChallenge(verifier);
        var challenge2 = OAuthPkce.CreateCodeChallenge(verifier);
        Assert.AreEqual(challenge1, challenge2);
        Assert.IsFalse(string.IsNullOrWhiteSpace(challenge1));
    }
}

[TestClass]
public sealed class OAuthReturnUrlValidatorTests
{
    [TestMethod]
    public void Normalize_allows_relative_paths_and_rejects_unknown_absolute_origins()
    {
        var validator = new OAuthReturnUrlValidator(
            Microsoft.Extensions.Options.Options.Create(
                new Full.NET.Modules.Identity.Configuration.IdentityOptions
                {
                    AllowedOrigins = ["https://admin.example.com"],
                }));

        Assert.AreEqual("/account/security", validator.Normalize("/account/security", "https://localhost:5173"));
        Assert.AreEqual(
            "https://admin.example.com/oauth/callback",
            validator.Normalize(
                "https://admin.example.com/oauth/callback",
                "https://localhost:5173"));
        Assert.IsNull(
            validator.Normalize(
                "https://evil.example.com/callback",
                "https://localhost:5173"));
    }
}
