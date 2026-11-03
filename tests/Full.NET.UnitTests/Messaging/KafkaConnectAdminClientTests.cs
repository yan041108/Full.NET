using System.Net;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

/// <summary>
/// 验证 Kafka Connect 管理客户端的状态判断与敏感信息保护。
/// </summary>
[TestClass]
public sealed class KafkaConnectAdminClientTests
{
    [TestMethod]
    public async Task Sql_server_offset_retains_transaction_change_position_and_serial()
    {
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK,
            """{"offsets":[{"partition":{"server":"source"},"offset":{"commit_lsn":"0000002d:00000288:0008","change_lsn":"0000002d:00000288:0006","event_serial_no":1}}]}"""))
        { BaseAddress = new("http://connect:8083/") };
        using var client = new KafkaConnectAdminClient(http);
        var position = await client.TryReadConnectorPositionAsync("pilot");
        Assert.AreEqual("0000002d:00000288:0008", position!.Lsn!.CommitLsn);
        Assert.AreEqual("0000002d:00000288:0006", position.Lsn.ChangeLsn);
        Assert.AreEqual(1L, position.Lsn.EventSerialNo);
    }

    [TestMethod]
    public async Task MySql_offset_preserves_completed_gtid_checkpoint_for_optional_time_observation()
    {
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK,
            """{"offsets":[{"partition":{"server":"source"},"offset":{"file":"mysql-bin.000001","pos":950,"gtids":"12345678-1234-1234-1234-123456789abc:1-12"}}]}"""))
        { BaseAddress = new("http://connect:8083/") };
        using var client = new KafkaConnectAdminClient(http);
        var position = await client.TryReadConnectorPositionAsync("pilot");
        Assert.AreEqual("12345678-1234-1234-1234-123456789abc:1-12", position!.Binlog!.CompletedGtidSet);
        Assert.AreEqual(950, position.Binlog.Position);
    }
    [TestMethod]
    public async Task Registration_creates_and_updates_the_same_connector_with_config_put()
    {
        using var handler = new RegistrationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://connect:8083/") };
        using var client = new KafkaConnectAdminClient(http);
        await client.RegisterConnectorAsync("scope-c", new Dictionary<string, string> { ["tasks.max"] = "1" });
        await client.RegisterConnectorAsync("scope-c", new Dictionary<string, string> { ["tasks.max"] = "2" });
        CollectionAssert.AreEqual(new[] { "1", "2" }, handler.TaskCounts.ToArray());
    }

    private sealed class RegistrationHandler : HttpMessageHandler
    {
        internal List<string> TaskCounts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual("/connectors/scope-c/config", request.RequestUri!.AbsolutePath);
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            TaskCounts.Add(body.RootElement.GetProperty("tasks.max").GetString()!);
            Assert.IsFalse(body.RootElement.TryGetProperty("config", out _), "配置 PUT 不接受创建接口的外层包装。");
            return new(TaskCounts.Count == 1 ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
    }

    [TestMethod]
    public async Task Register_connector_failure_does_not_disclose_response_body()
    {
        const string secret = "must-not-leak";
        using var httpClient = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.BadRequest,
            $"database.password={secret}"))
        {
            BaseAddress = new Uri("http://connect:8083/"),
        };
        using var client = new KafkaConnectAdminClient(httpClient);

        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.RegisterConnectorAsync(
                "scope-c",
                new Dictionary<string, string>
                {
                    ["database.password"] = secret,
                }));

        StringAssert.Contains(failure.Message, "400");
        Assert.IsFalse(failure.Message.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(failure.Message.Contains(
            "database.password",
            StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证仅顶层 Connector 暂停而任务仍运行时不得报告完整暂停。
    /// </summary>
    [TestMethod]
    public async Task Is_connector_paused_returns_false_while_task_is_still_running()
    {
        using var httpClient = CreateStatusClient(
            """
            {"connector":{"state":"PAUSED"},"tasks":[{"id":0,"state":"RUNNING"}]}
            """);
        using var client = new KafkaConnectAdminClient(httpClient);

        Assert.IsFalse(await client.IsConnectorPausedAsync("scope-c"));
    }

    /// <summary>
    /// 验证 Connector 与全部任务均暂停时报告完整暂停。
    /// </summary>
    [TestMethod]
    public async Task Is_connector_paused_returns_true_when_all_tasks_are_paused()
    {
        using var httpClient = CreateStatusClient(
            """
            {"connector":{"state":"PAUSED"},"tasks":[{"id":0,"state":"PAUSED"}]}
            """);
        using var client = new KafkaConnectAdminClient(httpClient);

        Assert.IsTrue(await client.IsConnectorPausedAsync("scope-c"));
    }

    /// <summary>
    /// 创建返回固定 Connector 状态的 HTTP 客户端。
    /// </summary>
    /// <param name="statusJson">Kafka Connect 状态响应。</param>
    /// <returns>配置了基础地址的 HTTP 客户端。</returns>
    private static HttpClient CreateStatusClient(string statusJson) =>
        new(new StaticResponseHandler(HttpStatusCode.OK, statusJson))
        {
            BaseAddress = new Uri("http://connect:8083/"),
        };

    /// <summary>
    /// 返回固定 HTTP 响应的测试消息处理器。
    /// </summary>
    /// <param name="statusCode">响应状态码。</param>
    /// <param name="body">响应正文。</param>
    private sealed class StaticResponseHandler(
        HttpStatusCode statusCode,
        string body) : HttpMessageHandler
    {
        /// <summary>
        /// 返回测试预设的 HTTP 响应。
        /// </summary>
        /// <param name="request">当前请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>固定响应。</returns>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body),
                RequestMessage = request,
            });
    }
}
