using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>使用真实授权管线与双库，验证成员只读、立即撤权及可信租户边界。</summary>
internal static class AiKnowledgeMembersApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.Update,
            AiKnowledgePermissions.PolicyUpdate, AiKnowledgePermissions.MembersRead, AiKnowledgePermissions.MembersUpdate,
            "tenancy.tenants.switch"];
        var owner = await factory.CreateHostIdentityAsync($"kb-owner-{Guid.NewGuid():N}", permissions);
        var member = await factory.CreateHostIdentityAsync($"kb-member-{Guid.NewGuid():N}", permissions);
        var stranger = await factory.CreateHostIdentityAsync($"kb-stranger-{Guid.NewGuid():N}", permissions);
        var noRead = await factory.CreateHostIdentityAsync($"kb-no-read-{Guid.NewGuid():N}", []);
        var disabled = await factory.CreateHostIdentityAsync($"kb-disabled-{Guid.NewGuid():N}", []);
        using var client = factory.CreateClientForHost("localhost");
        using var reader = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        using var administrator = factory.CreateClientForHost("localhost");
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([IdentityUserManagementPermissions.Disable]));
        using var disabledResponse = await administrator.PostAsync($"/api/v1/identity/users/{disabled.UserId}/disable", null);
        Assert.AreEqual(HttpStatusCode.OK, disabledResponse.StatusCode, await disabledResponse.Content.ReadAsStringAsync());
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("成员目录", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var catalog = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{catalog.Id:D}";
        var initial = (await client.GetFromJsonAsync<AiKnowledgeMembersResponse>(path + "/members"))!;
        Assert.HasCount(0, initial.UserIds);
        Assert.AreEqual(1, initial.Version);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);

        foreach (var invalid in new IReadOnlyList<Guid>?[] { null, [Guid.Empty], [owner.UserId], [member.UserId, member.UserId],
                     Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray() })
        {
            using var response = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest(invalid!, 1));
            await ProblemAsync(response, HttpStatusCode.BadRequest, AiKnowledgeErrorCodes.MembersInvalid);
        }
        using var unavailable = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([Guid.CreateVersion7()], 1));
        await ProblemAsync(unavailable, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.MemberUnavailable);
        using var inactive = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([disabled.UserId], 1));
        await ProblemAsync(inactive, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.MemberUnavailable);
        using var injection = await client.PutAsync(path + "/members", new StringContent(
            $"{{\"userIds\":[],\"version\":1,\"tenantId\":\"{Guid.CreateVersion7()}\"}}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, injection.StatusCode);
        Assert.AreEqual(1, (await client.GetFromJsonAsync<AiKnowledgeMembersResponse>(path + "/members"))!.Version);

        using var grant = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([member.UserId, noRead.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode, await grant.Content.ReadAsStringAsync());
        Assert.AreEqual(2, (await grant.Content.ReadFromJsonAsync<AiKnowledgeMembersResponse>())!.Version);
        using var mixedInvalid = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([member.UserId, disabled.UserId], 2));
        await ProblemAsync(mixedInvalid, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.MemberUnavailable);
        var preserved = (await client.GetFromJsonAsync<AiKnowledgeMembersResponse>(path + "/members"))!;
        Assert.AreEqual(2, preserved.Version);
        Assert.HasCount(2, preserved.UserIds);
        await ExpectAsync(reader, path, HttpStatusCode.OK);
        var page = (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!;
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(catalog.Id, page.Items[0].Id);
        // 成员即使持有所有写权限，仍不能修改他人的目录、审批或授权名单。
        await ExpectAsync(reader, path + "/members", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var deniedMembers = await reader.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], 2));
        await ProblemAsync(deniedMembers, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var deniedEdit = await reader.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("越权", null, true, 2));
        await ProblemAsync(deniedEdit, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var deniedPolicy = await reader.PutAsJsonAsync(path + "/policy", new UpdateAiKnowledgePolicyRequest("internal", null, null, null, null, 2));
        await ProblemAsync(deniedPolicy, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", noRead.AccessToken);
        await ExpectAsync(reader, path, HttpStatusCode.Forbidden, "authorization.permission_denied");
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stranger.AccessToken);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        Assert.HasCount(0, (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!.Items);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);

        using var stale = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], 1));
        await ProblemAsync(stale, HttpStatusCode.Conflict, AiKnowledgeErrorCodes.VersionConflict);
        using var disable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("成员目录", null, false, 2));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        await ExpectAsync(reader, path, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        Assert.HasCount(0, (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!.Items);
        using var enable = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeBaseRequest("成员目录", null, true, 3));
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        await ExpectAsync(reader, path, HttpStatusCode.OK);
        var edits = await Task.WhenAll(
            client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 4)),
            client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([stranger.UserId], 4)));
        try
        {
            Assert.AreEqual(1, edits.Count(item => item.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, edits.Count(item => item.StatusCode == HttpStatusCode.Conflict));
            var winner = (await edits.Single(item => item.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<AiKnowledgeMembersResponse>())!;
            var stored = (await client.GetFromJsonAsync<AiKnowledgeMembersResponse>(path + "/members"))!;
            Assert.AreEqual(5, stored.Version);
            CollectionAssert.AreEqual(winner.UserIds.ToArray(), stored.UserIds.ToArray());
        }
        finally { foreach (var edit in edits) edit.Dispose(); }
        using var revoke = await client.PutAsJsonAsync(path + "/members", new SetAiKnowledgeMembersRequest([], 5));
        Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
        foreach (var token in new[] { member.AccessToken, stranger.AccessToken })
        {
            reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await ExpectAsync(reader, path, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
            Assert.HasCount(0, (await reader.GetFromJsonAsync<PagedResult<AiKnowledgeBaseResponse>>("/api/v1/ai/knowledge-bases"))!.Items);
        }
        var unchanged = (await client.GetFromJsonAsync<AiKnowledgeBaseResponse>(path))!;
        Assert.IsNull(unchanged.EmbeddingModelConfigId);
        Assert.IsNull(unchanged.GenerationModelConfigId);
        Assert.AreEqual("internal", unchanged.DataClassification);

        // 所有权与端点权限独立；缺少成员写权限的所有者也不能授权。
        var limited = await factory.CreateHostIdentityAsync($"kb-limited-{Guid.NewGuid():N}", [AiKnowledgePermissions.Create]);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limited.AccessToken);
        using var limitedCreate = await reader.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("限权目录", null));
        Assert.AreEqual(HttpStatusCode.Created, limitedCreate.StatusCode);
        var limitedBase = (await limitedCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        using var permissionDenied = await reader.PutAsJsonAsync($"/api/v1/ai/knowledge-bases/{limitedBase.Id}/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        await ProblemAsync(permissionDenied, HttpStatusCode.Forbidden, "authorization.permission_denied");

        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        using var tenantReader = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, owner.AccessToken, tenant.Id);
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        await ExpectAsync(tenantClient, path + "/members", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var tenantCreate = await tenantClient.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("租户成员目录", null));
        Assert.AreEqual(HttpStatusCode.Created, tenantCreate.StatusCode);
        var tenantBase = (await tenantCreate.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var tenantPath = $"/api/v1/ai/knowledge-bases/{tenantBase.Id}";
        // 活动 Host 用户若不是当前租户活动成员，不能被授予租户知识库。
        using var crossScope = await tenantClient.PutAsJsonAsync(tenantPath + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        await ProblemAsync(crossScope, HttpStatusCode.UnprocessableEntity, AiKnowledgeErrorCodes.MemberUnavailable);
        await AddTenantMemberAsync(factory, tenant.Id, member.UserId);
        using var tenantGrant = await tenantClient.PutAsJsonAsync(tenantPath + "/members", new SetAiKnowledgeMembersRequest([member.UserId], 1));
        Assert.AreEqual(HttpStatusCode.OK, tenantGrant.StatusCode, await tenantGrant.Content.ReadAsStringAsync());
        var memberTenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(reader, member.AccessToken, tenant.Id);
        tenantReader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberTenantToken);
        await ExpectAsync(tenantReader, tenantPath, HttpStatusCode.OK);
        await ExpectAsync(tenantReader, path, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        await ExpectAsync(tenantReader, tenantPath + "/members", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var tenantRevoke = await tenantClient.PutAsJsonAsync(tenantPath + "/members", new SetAiKnowledgeMembersRequest([], 2));
        Assert.AreEqual(HttpStatusCode.OK, tenantRevoke.StatusCode);
        await ExpectAsync(tenantReader, tenantPath, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var switchBack = await tenantClient.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        Assert.AreEqual(HttpStatusCode.OK, switchBack.StatusCode);
        var hostToken = (await switchBack.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken.AccessToken);
        await ExpectAsync(client, tenantPath + "/members", HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
        using var crossWrite = await client.PutAsJsonAsync(tenantPath + "/members", new SetAiKnowledgeMembersRequest([], 3));
        await ProblemAsync(crossWrite, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.NotFound);
    }

    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode status, string? code = null)
    {
        using var response = await client.GetAsync(path);
        if (code is null) Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        else await ProblemAsync(response, status, code);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }

    // 仅测试夹具通过 Identity 自有执行语句建立真实成员；AI 生产代码只使用最小目录 Port。
    private static async Task AddTenantMemberAsync(FullNetApiFactory factory, Guid tenantId, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetTenant(new TenantContext(tenantId, "acme", "Acme Corporation"));
        try
        {
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            Assert.AreEqual(1, await command.ExecuteAsync(TenantMembershipSql.InsertMember,
                IdentitySqlParameters.Create(("Id", Guid.CreateVersion7()), ("UserId", userId),
                    ("MemberRole", TenantMemberRoles.Member), ("Status", TenantMemberStatuses.Active),
                    ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow), ("Version", 1))));
        }
        finally { current.Clear(); }
    }
}
