using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Security;
using Full.NET.IntegrationTests.Api;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>有效原会话在交接时仍必须完成真实密码和 TOTP 验证；复用回执不能再修改职责。</summary>
[TestClass]
public sealed class TenantOwnershipMfaTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Existing_session_requires_enrolled_TOTP_for_owner_handoff(DatabaseProvider provider)
    {
        var keyRing = Directory.CreateTempSubdirectory("fullnet-owner-totp-").FullName; byte[]? key = null;
        try
        {
            using var f = await TenantOwnershipHandoffTests.Fixture.CreateAsync(provider, extraSettings: new Dictionary<string, string?> {
                ["Identity:EnableTotpStrongReauthentication"] = "true", ["DataProtection:KeyRingPath"] = keyRing });
            var source = await f.AddPersonAsync("mfa-source", TenantMemberRoles.Owner); var target = await f.AddPersonAsync("mfa-target");
            var token = await f.TenantTokenAsync(source);
            // 夹具只登记实际密钥；原 HTTP 会话不变，被测 Endpoint 仍调用正式强认证 Provider。
            await using (var scope = f.Factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
                var service = scope.ServiceProvider.GetRequiredService<TotpEnrollmentService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", source.UserId.ToString("D"))], "fixture-enrollment"));
                var pending = await service.BeginAsync(principal); Assert.IsTrue(pending.IsSuccess, pending.Error?.Code);
                key = TotpAlgorithm.DecodeSharedSecret(pending.Value!.SharedSecretBase32);
                var confirmed = await service.ConfirmAsync(principal, TotpAlgorithm.ComputeCode(key, scope.ServiceProvider.GetRequiredService<IClock>().UtcNow));
                Assert.IsTrue(confirmed.IsSuccess, confirmed.Error?.Code);
            }
            Assert.IsTrue((await f.StateAsync(token)).RequiresTotp);
            var request = new TransferTenantOwnerRequest(Guid.CreateVersion7(), source.MemberId, 1, target.MemberId, 1, FullNetApiFactory.TestPassword, null);
            using (var missing = await f.TransferAsync(token, request)) Assert.AreEqual(HttpStatusCode.Unauthorized, missing.StatusCode);
            using (var wrong = await f.TransferAsync(token, request with { OperationId = Guid.CreateVersion7(), TotpCode = "invalid" }))
                Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.AreEqual(source.MemberId, (await f.StateAsync(token)).Owner!.Id);
            var code = TotpAlgorithm.ComputeCode(key, f.Factory.Services.GetRequiredService<IClock>().UtcNow);
            var handed = await f.SuccessAsync(token, request with { TotpCode = code });
            Assert.AreEqual(target.UserId, handed.TargetUserId); Assert.AreEqual(2, handed.TargetVersion);
            Assert.AreEqual(handed, await f.SuccessAsync(token, request with { CurrentPassword = "", TotpCode = null }));
            Assert.AreEqual(target.MemberId, (await f.StateAsync(token)).Owner!.Id); Assert.AreEqual(1L, await f.OwnerCountAsync());
        }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); if (Directory.Exists(keyRing)) Directory.Delete(keyRing, recursive: true); }
    }
}
