using System.Net;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

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

    private sealed class StaticResponseHandler(
        HttpStatusCode statusCode,
        string body) : HttpMessageHandler
    {
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
