using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>用场景数据验证可选渠道的原生读取，不交换 code、不创建外部审批或发送消息。</summary>
internal static class NativeApiNotificationChannelReadAssertions
{
    internal static async Task VerifyAsync(HttpClient client, string token, string logPath,
        DatabaseProvider provider, string connectionString, CancellationToken cancellationToken)
    {
        const string syncPath = "/api/v1/notifications/dingtalk/approval-sync";
        const string bindingPath = "/api/v1/notifications/wechat-miniprogram/bindings";
        var actor = await GetAsync("/api/v1/me");
        var actorId = actor.GetProperty("id").GetGuid();
        var syncIds = new List<Guid>();
        var bindingIds = new List<Guid>();
        var workflowIds = new List<Guid>();
        var profileIds = new List<Guid>();
        var endpointId = Guid.CreateVersion7();
        var when = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString,
                MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        for (var index = 0; index < 4; index++)
        {
            var syncId = Guid.CreateVersion7();
            var bindingId = Guid.CreateVersion7();
            syncIds.Add(syncId);
            bindingIds.Add(bindingId);
            workflowIds.Add(Guid.CreateVersion7());
            profileIds.Add(Guid.CreateVersion7());
            var full = index == 1;
            var parameters = new DynamicParameters();
            parameters.Add("SyncId", syncId);
            parameters.Add("BindingId", bindingId);
            parameters.Add("Scope", index == 3 ? $"tenant:{Guid.CreateVersion7():N}" : "host");
            parameters.Add("Actor", index == 2 ? Guid.CreateVersion7() : actorId);
            parameters.Add("WorkflowId", workflowIds[index]);
            parameters.Add("Key", $"native-channel-{syncId:N}");
            parameters.Add("ProcessId", full ? "external-process" : null);
            parameters.Add("Summary", full ? "原生读取摘要" : null);
            parameters.Add("Title", "原生审批镜像");
            parameters.Add("ExternalStatus", full ? "COMPLETED" : null);
            parameters.Add("ExternalResult", full ? "agree" : null);
            parameters.Add("Error", full ? "fixture-error" : null);
            object timestamp = provider == DatabaseProvider.MySql ? (object)when.UtcDateTime : when;
            parameters.Add("Created", timestamp);
            parameters.Add("Updated", full ? timestamp : null);
            parameters.Add("AppId", $"native-{index}");
            parameters.Add("ProfileId", profileIds[index]);
            parameters.Add("Hash", new string('a', 64));
            parameters.Add("UnionProtected", full ? "fixture-union-secret" : null);
            parameters.Add("UnionMask", full ? "union***" : null);
            parameters.Add("EndpointId", full ? endpointId : null);
            // 仅向测试新建库写入已完成镜像，轮询不会发起真实钉钉请求；受保护值仅作禁止泄露的哨兵。
            await database.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_notifications_dingtalk_approval_sync
                    (Id, TenantScopeKey, WorkflowInstanceId, IdempotencyKey, DingTalkProcessInstanceId,
                     ProcessCode, OriginatorUserId, DeptId, Title, Summary, StatusKey, ExternalStatusKey,
                     ExternalResultKey, LastErrorCode, LastSyncedAtUtc, CreatedAtUtc, UpdatedAtUtc, CreatedByUserId)
                VALUES (@SyncId, @Scope, @WorkflowId, @Key, @ProcessId, 'native-process', 'native-originator',
                    2147483648, @Title, @Summary, 'completed', @ExternalStatus, @ExternalResult,
                    @Error, @Updated, @Created, @Updated, @Actor);
                INSERT INTO fn_notifications_wechat_miniprogram_binding
                    (Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected, OpenIdMask,
                     OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey, RecipientEndpointId,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES (@BindingId, @Scope, @Actor, @AppId, @ProfileId, 'fixture-openid-secret', 'openid***',
                    @Hash, @UnionProtected, @UnionMask, 'verified', @EndpointId, @Created, @Updated);
                INSERT INTO fn_notifications_wechat_miniprogram_subscription
                    (BindingId, TemplateId, StatusKey, AuthorizedAtUtc)
                VALUES (@BindingId, 'native-template', 'accepted', @Created);
                """, parameters, cancellationToken: cancellationToken));
        }

        var syncs = (await GetAsync(syncPath)).GetProperty("items").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(syncIds.Take(3).ToArray(), syncs.Select(row => row.GetProperty("id").GetGuid()).ToArray());
        for (var index = 0; index < 2; index++)
        {
            var row = await GetAsync($"{syncPath}/{syncIds[index]}");
            Assert.AreEqual(syncIds[index], row.GetProperty("id").GetGuid());
            Assert.AreEqual(workflowIds[index], row.GetProperty("workflowInstanceId").GetGuid());
            Assert.AreEqual($"native-channel-{syncIds[index]:N}", row.GetProperty("idempotencyKey").GetString());
            Assert.AreEqual(2147483648L, row.GetProperty("deptId").GetInt64());
            Assert.AreEqual("native-process", row.GetProperty("processCode").GetString());
            Assert.AreEqual("native-originator", row.GetProperty("originatorUserId").GetString());
            Assert.AreEqual("原生审批镜像", row.GetProperty("title").GetString());
            Assert.AreEqual("completed", row.GetProperty("statusKey").GetString());
            Assert.AreEqual(when, row.GetProperty("createdAtUtc").GetDateTimeOffset());
            foreach (var property in new[] { "dingTalkProcessInstanceId", "summary", "externalStatusKey", "externalResultKey", "lastErrorCode", "lastSyncedAtUtc", "updatedAtUtc" })
                Assert.AreEqual(index == 0, row.GetProperty(property).ValueKind == JsonValueKind.Null, property);
            if (index == 1)
            {
                Assert.AreEqual("external-process", row.GetProperty("dingTalkProcessInstanceId").GetString());
                Assert.AreEqual("原生读取摘要", row.GetProperty("summary").GetString());
                Assert.AreEqual("COMPLETED", row.GetProperty("externalStatusKey").GetString());
                Assert.AreEqual("agree", row.GetProperty("externalResultKey").GetString());
                Assert.AreEqual("fixture-error", row.GetProperty("lastErrorCode").GetString());
                Assert.AreEqual(when, row.GetProperty("lastSyncedAtUtc").GetDateTimeOffset());
                Assert.AreEqual(when, row.GetProperty("updatedAtUtc").GetDateTimeOffset());
            }
        }
        await GetAsync($"{syncPath}/{syncIds[3]}", HttpStatusCode.NotFound);
        var bindings = (await GetAsync(bindingPath)).GetProperty("items").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(bindingIds.Take(3).ToArray(), bindings.Select(row => row.GetProperty("id").GetGuid()).ToArray());
        var mine = (await GetAsync(bindingPath + "/mine")).EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(bindingIds.Take(2).ToArray(), mine.Select(row => row.GetProperty("id").GetGuid()).ToArray());
        foreach (var row in mine)
        {
            var index = bindingIds.IndexOf(row.GetProperty("id").GetGuid());
            Assert.AreEqual(actorId, row.GetProperty("userId").GetGuid());
            Assert.AreEqual($"native-{index}", row.GetProperty("appId").GetString());
            Assert.AreEqual(profileIds[index], row.GetProperty("providerProfileVersionId").GetGuid());
            Assert.AreEqual("openid***", row.GetProperty("openIdMask").GetString());
            Assert.AreEqual("verified", row.GetProperty("verificationStatusKey").GetString());
            Assert.AreEqual(when, row.GetProperty("createdAtUtc").GetDateTimeOffset());
            if (index == 0)
            {
                Assert.AreEqual(JsonValueKind.Null, row.GetProperty("recipientEndpointId").ValueKind);
                Assert.AreEqual(JsonValueKind.Null, row.GetProperty("updatedAtUtc").ValueKind);
            }
            else
            {
                Assert.AreEqual(endpointId, row.GetProperty("recipientEndpointId").GetGuid());
                Assert.AreEqual(when, row.GetProperty("updatedAtUtc").GetDateTimeOffset());
            }
            var subscription = row.GetProperty("subscriptions").EnumerateArray().Single();
            Assert.AreEqual("native-template", subscription.GetProperty("templateId").GetString());
            Assert.AreEqual("accepted", subscription.GetProperty("statusKey").GetString());
            Assert.AreEqual(when, subscription.GetProperty("authorizedAtUtc").GetDateTimeOffset());
            Assert.IsFalse(row.ToString().Contains("fixture-openid-secret", StringComparison.Ordinal));
            Assert.IsFalse(row.ToString().Contains("fixture-union-secret", StringComparison.Ordinal));
        }
        await GetAsync(syncPath, HttpStatusCode.Unauthorized, anonymous: true);
        await GetAsync(bindingPath, HttpStatusCode.Unauthorized, anonymous: true);
        await GetAsync(bindingPath + "/mine", HttpStatusCode.Unauthorized, anonymous: true);

        async Task<JsonElement> GetAsync(string path, HttpStatusCode expected = HttpStatusCode.OK, bool anonymous = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, expected, "Native channel read " + path,
                cancellationToken, logPath);
            if (expected != HttpStatusCode.OK) return default;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.Clone();
        }
    }
}
