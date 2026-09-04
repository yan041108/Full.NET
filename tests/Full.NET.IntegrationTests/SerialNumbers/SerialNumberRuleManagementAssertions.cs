using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.IntegrationTests.SerialNumbers;

/// <summary>验收 Host 流水号规则 API 的授权、校验和乐观并发契约。</summary>
internal static class SerialNumberRuleManagementAssertions
{
    private const string RulesPath = "/api/v1/serial-numbers/rules";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(
                   RulesPath,
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var wrongToken = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        using (var forbidden = await client.SendAsync(
                   Authorized(HttpMethod.Get, RulesPath, wrongToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var reader = await factory.CreateHostIdentityAsync(
            $"serial-reader-{Guid.NewGuid():N}",
            [SerialNumberRulePermissions.Read],
            cancellationToken);
        var writer = await factory.CreateHostIdentityAsync(
            $"serial-writer-{Guid.NewGuid():N}",
            [
                SerialNumberRulePermissions.Read,
                SerialNumberRulePermissions.Create,
                SerialNumberRulePermissions.Update,
                SerialNumberRulePermissions.Enable,
                SerialNumberRulePermissions.Disable,
                SerialNumberRulePermissions.Preview,
            ],
            cancellationToken);
        using (var forbiddenWrite = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       RulesPath,
                       reader.AccessToken,
                       CreateRequest("forbidden.rule")),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
        }

        var created = await CreateAsync(
            client,
            writer.AccessToken,
            writer.UserId,
            cancellationToken);
        await VerifyListPreviewAndLifecycleAsync(
            client,
            writer.AccessToken,
            writer.UserId,
            created,
            cancellationToken);
        await OpenApiSerialNumbersContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task<SerialNumberRuleResponse> CreateAsync(
        HttpClient client,
        string token,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var key = $"invoice.{Guid.NewGuid():N}";
        var request = CreateRequest(key);
        using var response = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, RulesPath, token, request),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(key, created.RuleKey);
        Assert.AreEqual(actorUserId, created.CreatedByUserId);
        Assert.AreEqual(1, created.Version);

        using var duplicate = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, RulesPath, token, request),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.AreEqual(
            SerialNumberErrorCodes.RuleKeyExists,
            await ReadCodeAsync(duplicate, cancellationToken));
        return created;
    }

    private static async Task VerifyListPreviewAndLifecycleAsync(
        HttpClient client,
        string token,
        Guid actorUserId,
        SerialNumberRuleResponse created,
        CancellationToken cancellationToken)
    {
        using (var list = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       RulesPath + "?page=1&pageSize=100",
                       token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var page = await list.Content.ReadFromJsonAsync<
                PagedResult<SerialNumberRuleResponse>>(cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));
        }

        // 名称/键/状态筛选与稳定排序必须在服务端生效，不能依赖客户端二次过滤。
        using (var filtered = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       RulesPath
                       + $"?page=1&pageSize=20&name={Uri.EscapeDataString("发票")}"
                       + $"&key={Uri.EscapeDataString(created.RuleKey[..8])}"
                       + "&isEnabled=true&sortBy=ruleKey&sortDirection=asc",
                       token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, filtered.StatusCode);
            var page = await filtered.Content.ReadFromJsonAsync<
                PagedResult<SerialNumberRuleResponse>>(cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));
            Assert.IsTrue(page.Items.All(item =>
                item.DisplayName.Contains("发票", StringComparison.Ordinal)
                && item.IsEnabled));
        }

        using (var statusMiss = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       RulesPath
                       + $"?page=1&pageSize=20&key={Uri.EscapeDataString(created.RuleKey)}"
                       + "&isEnabled=false",
                       token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, statusMiss.StatusCode);
            var page = await statusMiss.Content.ReadFromJsonAsync<
                PagedResult<SerialNumberRuleResponse>>(cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsFalse(page.Items.Any(item => item.Id == created.Id));
        }

        using (var preview = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       RulesPath + "/preview",
                       token,
                       new PreviewSerialNumberRequest(
                           SerialNumberRuleScope.Tenant,
                           "INV-{utc:yyyy}-{tenant}-{sequence:5}",
                           "acme",
                           42,
                           new DateTimeOffset(
                               2026,
                               7,
                               30,
                               0,
                               0,
                               0,
                               TimeSpan.Zero),
                           SerialNumberResetInterval.Day)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
            var value = await preview.Content
                .ReadFromJsonAsync<SerialNumberPreviewResponse>(
                    cancellationToken);
            Assert.AreEqual("INV-2026-acme-00042", value?.Value);
            Assert.AreEqual("20260730", value?.ResetBucket);
            Assert.AreEqual(42L, value?.SequenceValue);
        }

        var update = new UpdateSerialNumberRuleRequest(
            "发票流水号（更新）",
            "UTC 月重置",
            SerialNumberRuleScope.Tenant,
            SerialNumberResetInterval.Month,
            "INV-{utc:yyyy}{utc:MM}-{tenant}-{sequence:5}",
            1,
            99999,
            20,
            true,
            created.Version);
        SerialNumberRuleResponse updated;
        using (var response = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Put,
                       $"{RulesPath}/{created.Id:D}",
                       token,
                       update),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            updated = (await response.Content
                .ReadFromJsonAsync<SerialNumberRuleResponse>(
                    cancellationToken))!;
            Assert.IsNotNull(updated);
            Assert.AreEqual(created.Version + 1, updated.Version);
            Assert.AreEqual(actorUserId, updated.UpdatedByUserId);
        }

        using (var stale = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Put,
                       $"{RulesPath}/{created.Id:D}",
                       token,
                       update),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.AreEqual(
                SerialNumberErrorCodes.RuleVersionConflict,
                await ReadCodeAsync(stale, cancellationToken));
        }

        using var disabled = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{RulesPath}/{created.Id:D}/disable",
                token,
                new ChangeSerialNumberRuleStatusRequest(updated.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        var disabledRule = await disabled.Content
            .ReadFromJsonAsync<SerialNumberRuleResponse>(cancellationToken);
        Assert.IsNotNull(disabledRule);
        Assert.IsFalse(disabledRule.IsEnabled);
    }

    private static CreateSerialNumberRuleRequest CreateRequest(string ruleKey) =>
        new(
            ruleKey,
            "发票流水号",
            null,
            SerialNumberRuleScope.Tenant,
            SerialNumberResetInterval.Day,
            "INV-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:5}",
            1,
            99999,
            10,
            true);

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<string?> ReadCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("code").GetString();
    }
}
