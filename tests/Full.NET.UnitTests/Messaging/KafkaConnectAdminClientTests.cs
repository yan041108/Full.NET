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
