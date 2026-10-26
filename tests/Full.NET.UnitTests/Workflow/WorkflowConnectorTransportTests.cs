using System.Net;
using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution.Connectors;

namespace Full.NET.UnitTests.Workflow;

/// <summary>连接器目录授权、DNS固定连接及副作用不确定结果必须独立于租约与网络重试。</summary>
[TestClass]
public sealed class WorkflowConnectorTransportTests
{
    private static WorkflowFormSchema Shape(string key, string kind) => new(1, 1, [new("main", [new(key, kind, true, new Dictionary<string, JsonElement>())])]);
    private static WorkflowHttpConnectorRegistration Registration(bool idempotent = true) => new("credit.check", 1,
        new Uri("https://connector.example.test/check"), new HashSet<string>(["host"], StringComparer.Ordinal),
        Shape("subject", "text"), Shape("score", "integer"), idempotent, 10, 3, "env:WORKFLOW_TEST_CONNECTOR_TOKEN");
    private static WorkflowBoundHttpConnector Bound(bool idempotent = true) => new WorkflowConnectorCatalog([Registration(idempotent)]).Find("host", "credit.check", 1)!;

    [TestMethod]
    public void Publish_binding_replaces_client_hash_and_checks_scope_and_parameter_types()
    {
        var catalog = new WorkflowConnectorCatalog([Registration()]);
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("title", "text", true, new Dictionary<string, JsonElement>()), new("count", "integer", false, new Dictionary<string, JsonElement>())])]);
        var config = JsonSerializer.SerializeToElement(new { boundConnectorHash = "untrusted-client-hash", fieldPolicies = new { title = "readOnly", count = "editable" },
            connectorPolicy = new { connectorKey = "credit.check", connectorVersion = 1, inputFields = new { subject = "title" }, outputFields = new { count = "score" } } });
        var draft = new WorkflowDefinitionDraft(1, [new("trigger", "integration.trigger", 1, config)], "automation");
        Assert.IsTrue(catalog.TryBindDraft("host", draft, schema, out var bound));
        Assert.AreEqual(catalog.Find("host", "credit.check", 1)!.ContentHash, bound!.Nodes[0].Config.GetProperty("boundConnectorHash").GetString());
        Assert.AreEqual("untrusted-client-hash", draft.Nodes[0].Config.GetProperty("boundConnectorHash").GetString());
        Assert.IsFalse(catalog.TryBindDraft("tenant:0191234567897abc8def0123456789ab", draft, schema, out _));
        var wrong = new WorkflowConnectorCatalog([Registration() with { RequestSchema = Shape("subject", "integer") }]);
        Assert.IsFalse(wrong.TryBindDraft("host", draft, schema, out _));
    }

    [TestMethod]
    public void Malformed_legacy_drafts_are_left_for_graph_compilation_without_dereferencing_them()
    {
        var catalog = new WorkflowConnectorCatalog([]); var draft = new WorkflowDefinitionDraft(1, null!);
        Assert.IsTrue(catalog.TryBindDraft("host", draft, Shape("subject", "text"), out var bound)); Assert.AreSame(draft, bound);
        Assert.IsFalse(WorkflowDefinitionCompiler.Compile(bound!).IsSuccess);
    }

    [TestMethod]
    public void Catalog_freezes_scope_and_configuration_and_never_falls_back_to_another_version()
    {
        var scope = new HashSet<string>(["host"], StringComparer.Ordinal);
        var registration = Registration() with { AllowedScopes = scope };
        var catalog = new WorkflowConnectorCatalog([registration]); var value = catalog.Find("host", "credit.check", 1)!;
        scope.Add("tenant:0191234567897abc8def0123456789ab");
        Assert.IsNull(catalog.Find("tenant:0191234567897abc8def0123456789ab", "credit.check", 1));
        Assert.IsNull(catalog.Find("host", "credit.check", 2)); Assert.IsNull(catalog.Find("host", "credit.check", 1, "wrong"));
        Assert.AreSame(value, catalog.Find("host", "credit.check", 1, value.ContentHash));
        var changed = new WorkflowConnectorCatalog([registration with { Target = new Uri("https://connector.example.test/changed") }]);
        Assert.AreNotEqual(value.ContentHash, changed.Find("host", "credit.check", 1)!.ContentHash);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowConnectorCatalog([registration, registration]));
    }

    [TestMethod]
    [DataRow("https://localhost/check")]
    [DataRow("https://localhost./check")]
    [DataRow("https://service.local/check")]
    [DataRow("https://metadata.google.internal/check")]
    [DataRow("https://127.0.0.1/check")]
    [DataRow("https://10.0.0.1/check")]
    [DataRow("https://169.254.169.254/check")]
    [DataRow("https://168.63.129.16/check")]
    [DataRow("https://[::ffff:192.168.1.1]/check")]
    [DataRow("https://[fd00::1]/check")]
    [DataRow("https://user:password@example.test/check")]
    [DataRow("http://example.test/check")]
    [DataRow("https://example.test/check#fragment")]
    public void Non_public_or_credential_bearing_targets_are_rejected(string url)
    {
        Assert.IsFalse(WorkflowConnectorNetwork.AllowsTarget(new Uri(url)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowConnectorCatalog([Registration() with { Target = new Uri(url) }]));
    }

    [TestMethod]
    public async Task Mixed_dns_answers_are_rejected_before_any_socket_is_opened()
    {
        var opened = 0;
        var network = new WorkflowConnectorNetwork((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.1") }),
            (_, _) => { opened++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsExactlyAsync<WorkflowConnectorDestinationException>(() => network.ConnectAsync(new Uri("https://example.test/"), new("example.test", 443), default).AsTask());
        Assert.AreEqual(0, opened);
    }

    [TestMethod]
    public async Task Socket_connects_the_exact_validated_ip_and_rejects_target_substitution()
    {
        IPEndPoint? opened = null;
        var network = new WorkflowConnectorNetwork((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }),
            (endpoint, _) => { opened = endpoint; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await using var stream = await network.ConnectAsync(new Uri("https://example.test/"), new("example.test", 443), default);
        Assert.AreEqual("93.184.216.34", opened!.Address.ToString());
        await Assert.ThrowsExactlyAsync<WorkflowConnectorDestinationException>(() => network.ConnectAsync(new Uri("https://example.test/"), new("other.test", 443), default).AsTask());
    }

    [TestMethod]
    [DataRow(400, false, "rejected")]
    [DataRow(401, true, "rejected")]
    [DataRow(500, false, "outcome_unknown")]
    [DataRow(500, true, "retryable")]
    [DataRow(429, false, "outcome_unknown")]
    [DataRow(302, true, "outcome_unknown")]
    [DataRow(200, true, "succeeded")]
    public async Task Remote_status_and_idempotency_capability_determine_safe_outcome(int status, bool idempotent, string expected)
    {
        var client = new WorkflowConnectorHttpClient(_ => new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"score\":7}") }))), _ => "test-token");
        var result = await client.SendAsync(Bound(idempotent), Guid.CreateVersion7(), "{\"subject\":\"Example\"}", default);
        Assert.AreEqual(expected, result.OutcomeKey);
    }

    [TestMethod]
    [DataRow(false, "outcome_unknown")]
    [DataRow(true, "retryable")]
    public async Task Timeout_does_not_claim_the_remote_side_effect_was_rolled_back(bool idempotent, string expected)
    {
        var client = new WorkflowConnectorHttpClient(_ => new HttpClient(new Handler((_, _) => throw new TaskCanceledException("remote result unavailable"))), _ => "test-token");
        Assert.AreEqual(expected, (await client.SendAsync(Bound(idempotent), Guid.CreateVersion7(), "{\"subject\":\"Example\"}", default)).OutcomeKey);
    }

    [TestMethod]
    public async Task Stable_activation_key_and_secret_reference_are_kept_out_of_request_body_and_results()
    {
        var keys = new List<string>(); var activation = Guid.CreateVersion7();
        var client = new WorkflowConnectorHttpClient(_ => new HttpClient(new Handler(async (request, token) => {
            keys.Add(request.Headers.GetValues("Idempotency-Key").Single()); Assert.AreEqual("test-token", request.Headers.Authorization!.Parameter);
            Assert.AreEqual("{\"subject\":\"Example\"}", await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"score\":7}") };
        })), reference => reference == "env:WORKFLOW_TEST_CONNECTOR_TOKEN" ? "test-token" : null);
        var first = await client.SendAsync(Bound(), activation, "{\"subject\":\"Example\"}", default);
        var second = await client.SendAsync(Bound(), activation, "{\"subject\":\"Example\"}", default);
        CollectionAssert.AreEqual(new[] { activation.ToString("D"), activation.ToString("D") }, keys.ToArray());
        Assert.AreEqual(first, second); Assert.AreEqual("{\"score\":7}", first.ResponseJson);
    }

    [TestMethod]
    public async Task Invalid_input_secret_or_response_never_becomes_a_retryable_business_success()
    {
        var sent = 0;
        var client = new WorkflowConnectorHttpClient(_ => new HttpClient(new Handler((_, _) => { sent++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"score\":\"wrong type\"}") }); })), _ => "test-token");
        Assert.AreEqual("rejected", (await client.SendAsync(Bound(), Guid.CreateVersion7(), "{\"subject\":1}", default)).OutcomeKey); Assert.AreEqual(0, sent);
        Assert.AreEqual("failed", (await client.SendAsync(Bound(), Guid.CreateVersion7(), "{\"subject\":\"Example\"}", default)).OutcomeKey); Assert.AreEqual(1, sent);
        var missing = new WorkflowConnectorHttpClient(_ => throw new InvalidOperationException("must not send"), _ => null);
        Assert.AreEqual("rejected", (await missing.SendAsync(Bound(), Guid.CreateVersion7(), "{\"subject\":\"Example\"}", default)).OutcomeKey);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
