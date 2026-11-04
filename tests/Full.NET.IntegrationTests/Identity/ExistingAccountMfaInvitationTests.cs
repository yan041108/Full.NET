using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>已有账号经真实中心 MFA 与 BFF 回调后，明确接受原邀请并进入自己的企业。</summary>
[TestClass]
public sealed class ExistingAccountMfaInvitationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Mfa_login_retains_invitation_until_explicit_acceptance(DatabaseProvider provider)
    {
        var settings = IdentityBrowserTestSupport.Settings();
        var keyRing = Path.Combine(Path.GetTempPath(), "fullnet-mfa-invitation-" + Guid.NewGuid().ToString("N"));
        settings["DataProtection:KeyRingPath"] = keyRing;
        byte[]? key = null;
        try
        {
            var transport = new IdentityBrowserTestTransport();
            using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), settings,
                configureTestServices: services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport));
            transport.Factory = factory;
            await factory.InitializeAsync();
            var username = "mfa-invitee-" + Guid.NewGuid().ToString("N");
            var account = await factory.CreateHostIdentityAsync(username, [], password: FullNetApiFactory.TestPassword);
            var email = username + "@example.test";
            await factory.EnsureHostUserProfileEmailAsync(account.UserId, email, "MFA invited account");
            using var tenantClient = factory.CreateClientForHost("acme.localhost");
            var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);

            using var manager = IdentityBrowserTestSupport.Browser(factory);
            await IdentityBrowserTestSupport.LoginBrowserAsync(manager);
            using (var changed = await MutateAsync(manager, HttpMethod.Put, "/api/v1/tenancy/context", new ChangeTenantContextRequest(tenant.Id)))
                Assert.AreEqual(HttpStatusCode.NoContent, changed.StatusCode);
            using var created = await MutateAsync(manager, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations",
                new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
            var invitation = await created.Content.ReadFromJsonAsync<CreateTenantInvitationResult>();
            Assert.IsNotNull(invitation);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
                var enrollment = scope.ServiceProvider.GetRequiredService<TotpEnrollmentService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", account.UserId.ToString("D"))], "test-enrollment"));
                var pending = await enrollment.BeginAsync(principal);
                Assert.IsTrue(pending.IsSuccess, pending.Error?.Code);
                key = TotpAlgorithm.DecodeSharedSecret(pending.Value!.SharedSecretBase32);
                var enabled = await enrollment.ConfirmAsync(principal,
                    TotpAlgorithm.ComputeCode(key, scope.ServiceProvider.GetRequiredService<IClock>().UtcNow));
                Assert.IsTrue(enabled.IsSuccess, enabled.Error?.Code);
            }

            using var browser = IdentityBrowserTestSupport.Browser(factory);
            using var start = await browser.GetAsync("/api/v1/identity/browser/login");
            Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
            using var login = await browser.GetAsync(start.Headers.Location);
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
            var fields = IdentityBrowserTestSupport.Hidden(await login.Content.ReadAsStringAsync());
            fields["username"] = username; fields["password"] = FullNetApiFactory.TestPassword;
            // 正确密码仍不能绕过第二因子；失败后使用页面重新签发的隐藏字段。
            using var missingFactor = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
            Assert.AreEqual(HttpStatusCode.Unauthorized, missingFactor.StatusCode);
            Assert.IsNull(missingFactor.Headers.Location);
            using (var denied = await browser.GetAsync("/api/v1/me")) Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
            fields = IdentityBrowserTestSupport.Hidden(await missingFactor.Content.ReadAsStringAsync());
            fields["username"] = username; fields["password"] = FullNetApiFactory.TestPassword;
            // 使用正式时钟和真实登记密钥，不替换 MFA 校验器或伪造授权结果。
            fields["second_factor"] = TotpAlgorithm.ComputeCode(key, factory.Services.GetRequiredService<IClock>().UtcNow);
            using var authorized = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
            Assert.AreEqual(HttpStatusCode.Redirect, authorized.StatusCode);
            Assert.IsNotNull(authorized.Headers.Location);
            using var callback = await browser.GetAsync(authorized.Headers.Location);
            Assert.AreEqual(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.AreEqual("/#/identity/oidc/callback", callback.Headers.Location?.OriginalString);
            using (var me = await browser.GetAsync("/api/v1/me"))
            {
                Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
                using var user = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
                Assert.AreEqual(account.UserId, user.RootElement.GetProperty("id").GetGuid());
                Assert.AreEqual(JsonValueKind.Null, user.RootElement.GetProperty("tenantId").ValueKind);
            }
            using var pendingInvitations = await browser.GetAsync("/api/v1/me/tenant-invitations");
            Assert.AreEqual(HttpStatusCode.OK, pendingInvitations.StatusCode);
            var items = await pendingInvitations.Content.ReadFromJsonAsync<PagedResult<MyTenantInvitationResponse>>();
            Assert.IsNotNull(items);
            // 资料邮箱不是归属证明；登录不能自动认领尚未凭邀请令牌绑定的邀请。
            Assert.IsFalse(items.Items.Any(item => item.Id == invitation.Invitation.Id));
            using (var available = await browser.GetAsync("/api/v1/tenancy/available"))
            {
                Assert.AreEqual(HttpStatusCode.OK, available.StatusCode);
                using var tenants = JsonDocument.Parse(await available.Content.ReadAsStringAsync());
                Assert.IsFalse(tenants.RootElement.EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == tenant.Id));
            }
            using var accepted = await MutateAsync(browser, HttpMethod.Post,
                "/api/v1/identity/tenant-invitations/accept", new AcceptTenantInvitationRequest(invitation.InvitationToken));
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
            var member = await accepted.Content.ReadFromJsonAsync<AcceptTenantInvitationResponse>();
            Assert.IsNotNull(member);
            Assert.AreEqual(account.UserId, member.UserId); Assert.AreEqual(tenant.Id, member.TenantId);
            Assert.AreEqual(TenantMemberStatuses.Active, member.Status);
            using var entered = await MutateAsync(browser, HttpMethod.Put, "/api/v1/tenancy/context", new ChangeTenantContextRequest(tenant.Id));
            Assert.AreEqual(HttpStatusCode.NoContent, entered.StatusCode);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(keyRing)) Directory.Delete(keyRing, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient browser, HttpMethod method, string path, object body)
    {
        using var state = await browser.GetAsync("/api/v1/identity/browser/session");
        Assert.AreEqual(HttpStatusCode.OK, state.StatusCode);
        using var payload = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-CSRF-Token", payload.RootElement.GetProperty("csrfToken").GetString());
        return await browser.SendAsync(request);
    }
}
