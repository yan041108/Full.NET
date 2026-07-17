using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class JwtAccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 2, 3, 4, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981a3f-00c0-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981a3f-00c0-7000-8000-000000000002");
    private static readonly Guid TokenId =
        Guid.Parse("01981a3f-00c0-7000-8000-000000000003");
    private static readonly Guid TenantId =
        Guid.Parse("01981a3f-00c0-7000-8000-000000000004");

    [TestMethod]
    public async Task Issued_token_has_kid_required_claims_and_valid_signature()
    {
        var options = new IdentityOptions
        {
            AllowDevelopmentEphemeralSigningKey = true,
        };
        using var keyRing = new RsaSigningKeyRing(
            Options.Create(options),
            NullLogger<RsaSigningKeyRing>.Instance);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(TokenId);
        var issuer = new JwtAccessTokenIssuer(
            Options.Create(options),
            keyRing,
            new FixedClock(),
            idGenerator);
        var user = new IdentityUser(
            UserId,
            null,
            "host",
            "admin",
            "ADMIN",
            "系统管理员",
            "hash",
            true,
            0,
            null,
            "security-stamp",
            Now,
            null,
            1);

        var issued = issuer.Issue(
            user,
            SessionId,
            TenantId,
            ["z.permission", "a.permission", "z.permission"]);
        var token = new JsonWebToken(issued.AccessToken);

        Assert.IsFalse(string.IsNullOrWhiteSpace(token.Kid));
        Assert.AreEqual(options.Issuer, token.Issuer);
        CollectionAssert.Contains(token.Audiences.ToArray(), options.Audience);
        Assert.AreEqual(UserId.ToString("D"), token.Subject);
        Assert.AreEqual(TokenId.ToString("D"), token.GetClaim(JwtRegisteredClaimNames.Jti).Value);
        Assert.AreEqual(options.ClientId, token.GetClaim("client_id").Value);
        Assert.AreEqual(SessionId.ToString("D"), token.GetClaim(IdentityClaimTypes.SessionId).Value);
        Assert.AreEqual("host", token.GetClaim(IdentityClaimTypes.ActorScope).Value);
        Assert.AreEqual(
            $"tenant:{TenantId:N}",
            token.GetClaim(IdentityClaimTypes.Scope).Value);
        Assert.AreEqual(
            TenantId.ToString("D"),
            token.GetClaim(IdentityClaimTypes.TenantId).Value);
        CollectionAssert.AreEqual(
            new[] { "a.permission", "z.permission" },
            token.Claims
                .Where(claim => claim.Type == IdentityClaimTypes.Permission)
                .Select(claim => claim.Value)
                .ToArray());
        Assert.AreEqual("security-stamp", token.GetClaim(IdentityClaimTypes.SecurityStamp).Value);
        Assert.AreEqual(Now.AddMinutes(10), issued.ExpiresAtUtc);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            issued.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keyRing.ValidationKeys,
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = false,
            });
        Assert.IsTrue(validation.IsValid, validation.Exception?.Message);
    }

    [TestMethod]
    public void Configured_rsa_key_shorter_than_2048_bits_is_rejected()
    {
        using var rsa = RSA.Create(1024);
        var options = new IdentityOptions
        {
            ActiveKeyId = "weak-key",
            SigningKeys = new Dictionary<string, IdentitySigningKeyOptions>
            {
                ["weak-key"] = new()
                {
                    PublicKeyPem = rsa.ExportRSAPublicKeyPem(),
                    PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
                },
            },
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RsaSigningKeyRing(
                Options.Create(options),
                NullLogger<RsaSigningKeyRing>.Instance));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
