using Full.NET.Hosting.Observability;

namespace Full.NET.UnitTests.Hosting;

/// <summary>Elasticsearch 节点端点脱敏测试。</summary>
[TestClass]
public sealed class ElasticsearchEndpointRedactorTests
{
    [TestMethod]
    public void Redact_returns_authority_without_credentials()
    {
        var redacted = ElasticsearchEndpointRedactor.Redact("https://search.example.com:9243");
        Assert.AreEqual("https://search.example.com:9243", redacted?.TrimEnd('/'));
    }

    [TestMethod]
    public void Redact_rejects_invalid_uri()
    {
        Assert.IsNull(ElasticsearchEndpointRedactor.Redact("not-a-uri"));
    }
}
