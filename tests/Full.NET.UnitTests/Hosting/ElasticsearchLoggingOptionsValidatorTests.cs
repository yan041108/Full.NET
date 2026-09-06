using Full.NET.Hosting.Observability;

namespace Full.NET.UnitTests.Hosting;

/// <summary>Elasticsearch 日志 Sink 配置校验测试。</summary>
[TestClass]
public sealed class ElasticsearchLoggingOptionsValidatorTests
{
    private readonly ElasticsearchLoggingOptionsValidator _validator = new();

    [TestMethod]
    public void Validate_allows_disabled_configuration()
    {
        var result = _validator.Validate(
            null,
            new ElasticsearchLoggingOptions { Enabled = false });
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_rejects_enabled_configuration_without_nodes()
    {
        var result = _validator.Validate(
            null,
            new ElasticsearchLoggingOptions { Enabled = true, NodeUris = [] });
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void Validate_rejects_node_uri_with_embedded_credentials()
    {
        var result = _validator.Validate(
            null,
            new ElasticsearchLoggingOptions
            {
                Enabled = true,
                NodeUris = ["http://user:pass@localhost:9200"],
                IndexFormat = "fullnet-logs-{0:yyyy.MM.dd}",
            });
        Assert.IsFalse(result.Succeeded);
    }
}
