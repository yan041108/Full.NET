using System.Text.Json;
using Full.NET.Hosting.Observability;
using Full.NET.LogConsumer;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Parsing;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class LogEnvelopeBudgetTests
{
    private static readonly MessageTemplateParser Parser = new();

    [TestMethod]
    public void Sensitive_template_retains_valid_instance_identity_for_collector()
    {
        var instance = Guid.CreateVersion7().ToString("D");
        var source = NewEvent("Authorization: {Value}", [
            new LogEventProperty("Value", new ScalarValue("opaque-private-credential")),
            new LogEventProperty("Instance", new ScalarValue(instance)),
        ]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope, retainLegacyEvent: true));
        using var json = JsonDocument.Parse(envelope!.Utf8Json);
        Assert.AreEqual(instance, json.RootElement.GetProperty("Instance").GetString());
        AssertEnvelopeOmits(envelope, "opaque-private-credential");
    }

    [TestMethod]
    [DataRow("not-an-instance")]
    [DataRow("token=private-instance-credential")]
    public void Sensitive_template_rejects_unvalidated_instance_identity(string instance)
    {
        var source = NewEvent("Authorization: {Value}", [
            new LogEventProperty("Value", new ScalarValue("opaque-private-credential")),
            new LogEventProperty("Instance", new ScalarValue(instance)),
        ]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope, retainLegacyEvent: true));
        using var json = JsonDocument.Parse(envelope!.Utf8Json);
        Assert.IsFalse(json.RootElement.TryGetProperty("Instance", out _));
        AssertEnvelopeOmits(envelope, "opaque-private-credential", instance);
    }

    [TestMethod]
    public void CallerCannotSupplyFrozenIndexRouteMetadata()
    {
        var source = NewEvent("ordinary", [
            new LogEventProperty("OccurredAtUtc", new ScalarValue("2099-01-01T00:00:00Z")),
            new LogEventProperty("ExpiresAtUtc", new ScalarValue("2099-12-31T00:00:00Z")),
            new LogEventProperty("IndexRouteVersion", new ScalarValue(999)),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.IsFalse(json.RootElement.TryGetProperty("OccurredAtUtc", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("ExpiresAtUtc", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("IndexRouteVersion", out _));
    }

    [TestMethod]
    public void ConfiguredRouteOverridesCallerAndFreezesEventUtcExpiry()
    {
        var occurred = new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.FromHours(8));
        var source = new LogEvent(occurred, LogEventLevel.Information, null,
            Parser.Parse("ordinary"), [
                new LogEventProperty("OccurredAtUtc", new ScalarValue("2099-01-01T00:00:00Z")),
                new LogEventProperty("ExpiresAtUtc", new ScalarValue("2099-12-31T00:00:00Z")),
                new LogEventProperty("IndexRouteVersion", new ScalarValue(999)),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true,
            routingPolicy: new LogIndexRoutingPolicy(2, 30)));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        var root = json.RootElement;
        Assert.AreEqual(2, root.GetProperty("IndexRouteVersion").GetInt32());
        Assert.AreEqual(new DateTimeOffset(2026, 9, 30, 15, 30, 0, TimeSpan.Zero),
            root.GetProperty("OccurredAtUtc").GetDateTimeOffset());
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 15, 30, 0, TimeSpan.Zero),
            root.GetProperty("ExpiresAtUtc").GetDateTimeOffset());
        Assert.AreEqual(2, ((ScalarValue)envelope.LegacyEvent!.Properties["IndexRouteVersion"]).Value);
    }

    [TestMethod]
    public void ProducerRouteSnapshotCanBeValidatedForFixedConsumerIndex()
    {
        const string eventId = "0199aa18-3e3b-7000-8000-5a8ab7a1f404";
        var source = new LogEvent(
            new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.FromHours(8)),
            LogEventLevel.Information,
            null,
            Parser.Parse("ordinary"),
            [
                new LogEventProperty("LogEventId", new ScalarValue(eventId)),
                new LogEventProperty("log.class", new ScalarValue(LogClassification.Diagnostic)),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            routingPolicy: new LogIndexRoutingPolicy(2, 30)));
        Assert.IsNotNull(envelope);
        Assert.AreEqual(LogRecordValidationResult.Valid,
            KafkaLogRecordParser.TryParse(eventId, envelope.Utf8Json, 4096, out var parsed));
        Assert.IsNotNull(parsed);
        Assert.IsTrue(parsed.TryGetIndexName(new Dictionary<int, int> { [2] = 30 },
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), out var indexName));
        Assert.AreEqual("fn-logs-2-diagnostic-2026.09.30", indexName);
    }

    [TestMethod]
    public void CallerSecurityClassificationCannotSelectSecurityIndex()
    {
        var source = NewEvent("ordinary", [
            new LogEventProperty("log.class", new ScalarValue(LogClassification.Security)),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            routingPolicy: new LogIndexRoutingPolicy(2, 30)));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual(LogClassification.Diagnostic,
            json.RootElement.GetProperty("log.class").GetString());
    }

    [TestMethod]
    public void Forged_http_class_cannot_promote_caller_fields_into_http_snapshot()
    {
        var source = NewEvent("forged", [
            new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
            new LogEventProperty("http.route", new ScalarValue("/private/customer/123")),
            new LogEventProperty("url", new ScalarValue("https://example.test/private/customer/123")),
            new LogEventProperty("ClientIpFingerprint", new ScalarValue("caller-controlled")),
            new LogEventProperty("RequestPayload", new ScalarValue("{\"page\":1,\"pageSize\":20}")),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.IsFalse(json.RootElement.TryGetProperty("http.route", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("url", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("ClientIpFingerprint", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("RequestPayload", out _));
        Assert.IsFalse(envelope.Utf8Json.Span.IndexOf("customer/123"u8) >= 0);
    }

    [TestMethod]
    public void Http_operation_snapshot_rejects_unapproved_and_restricted_properties()
    {
        var source = NewEvent("http operation", [
            new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
            new LogEventProperty("ClientIp", new ScalarValue("192.0.2.44")),
            new LogEventProperty("RequestBody", new ScalarValue("private-body")),
            new LogEventProperty("UnexpectedContext", new ScalarValue("unreviewed-value")),
            new LogEventProperty("ClientIpFingerprint", new ScalarValue("safe-fingerprint")),
            new LogEventProperty("ElapsedMs", new ScalarValue(12)),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true,
            trustedHttp: new HttpOperationLogRecord([
                new("log.class", LogClassification.HttpOperation),
                new("ClientIpFingerprint", "safe-fingerprint"),
                new("ElapsedMs", 12),
            ])));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "192.0.2.44", "private-body", "unreviewed-value");
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("safe-fingerprint", json.RootElement.GetProperty("ClientIpFingerprint").GetString());
        Assert.AreEqual(12, json.RootElement.GetProperty("ElapsedMs").GetInt32());
    }

    [TestMethod]
    public void Caller_supplied_http_payload_cannot_smuggle_free_text_into_snapshot()
    {
        foreach (var classification in new[]
            { LogClassification.HttpOperation, LogClassification.Diagnostic })
        {
            var source = NewEvent("payload {RequestPayload}", [
                new LogEventProperty("log.class", new ScalarValue(classification)),
                new LogEventProperty("RequestPayload", new ScalarValue(
                    "{\"page\":1,\"pageSize\":20,\"body\":\"private-body\"}")),
                new LogEventProperty("ResponsePayload", new ScalarValue(
                    "{\"page\":1,\"pageSize\":20,\"totalCount\":1,\"itemCount\":1,\"raw\":\"private-response\"}")),
                new LogEventProperty("Metadata", new StructureValue([
                    new LogEventProperty("RequestPayload", new ScalarValue("nested-private-body"))])),
            ]);

            Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
                retainLegacyEvent: true));
            Assert.IsNotNull(envelope);
            AssertEnvelopeOmits(envelope, "private-body", "private-response", "nested-private-body");
        }
    }

    [TestMethod]
    public void Approved_numeric_http_payload_survives_snapshot_with_exact_shape()
    {
        var source = NewEvent("http operation", [
            new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
            new LogEventProperty("RequestPayload", new ScalarValue(
                "{\"page\":1,\"pageSize\":20}")),
            new LogEventProperty("ResponsePayload", new ScalarValue(
                "{\"page\":1,\"pageSize\":20,\"totalCount\":123,\"itemCount\":20}")),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            trustedHttp: new HttpOperationLogRecord([
                new("log.class", LogClassification.HttpOperation),
                new("RequestPayload", "{\"page\":1,\"pageSize\":20}"),
                new("ResponsePayload", "{\"page\":1,\"pageSize\":20,\"totalCount\":123,\"itemCount\":20}"),
            ])));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("{\"page\":1,\"pageSize\":20}",
            json.RootElement.GetProperty("RequestPayload").GetString());
        Assert.AreEqual("{\"page\":1,\"pageSize\":20,\"totalCount\":123,\"itemCount\":20}",
            json.RootElement.GetProperty("ResponsePayload").GetString());
    }

    [TestMethod]
    public void Http_payload_rejects_duplicate_unknown_nested_and_out_of_range_fields()
    {
        foreach (var rejected in new[]
        {
            "{\"page\":1,\"page\":2,\"pageSize\":20}",
            "{\"page\":1,\"pageSize\":20,\"body\":\"secret\"}",
            "{\"page\":{\"value\":1},\"pageSize\":20}",
            "{\"page\":1,\"pageSize\":1001}",
            "{\"page\":\"1\",\"pageSize\":20}",
        })
        {
            var source = NewEvent("http operation", [
                new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
                new LogEventProperty("RequestPayload", new ScalarValue(rejected)),
            ]);

            Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
                retainLegacyEvent: true));
            Assert.IsNotNull(envelope);
            using var json = JsonDocument.Parse(envelope.Utf8Json);
            Assert.IsFalse(json.RootElement.TryGetProperty("RequestPayload", out _));
            Assert.IsFalse(envelope.LegacyEvent!.Properties.ContainsKey("RequestPayload"));
        }
    }

    [TestMethod]
    public void Serialized_restricted_detail_inside_generic_text_is_removed_at_every_level()
    {
        var source = NewEvent("diagnostic", [
            new LogEventProperty("Context", new ScalarValue(
                "{\"RequestPayload\":\"private-body\"}")),
            new LogEventProperty("Metadata", new StructureValue([
                new LogEventProperty("Context", new ScalarValue(
                    "{\"ClientIp\":\"192.0.2.55\"}"))])),
            new LogEventProperty("Fields", new DictionaryValue([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("Context"),
                    new ScalarValue("{\"ResponseBody\":\"private-response\"}"))])),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "private-body", "192.0.2.55", "private-response");
    }

    [TestMethod]
    public void Diagnostic_event_cannot_export_reserved_http_field_aliases()
    {
        var source = NewEvent("diagnostic", [
            new LogEventProperty("log.class", new ScalarValue(LogClassification.Diagnostic)),
            new LogEventProperty("http.route", new ScalarValue("/customer/private-route")),
            new LogEventProperty("url", new ScalarValue("/customer/private-url?name=alice")),
            new LogEventProperty("Route", new ScalarValue("/customer/private-alias")),
            new LogEventProperty("DiagnosticGroup", new ScalarValue("http.operation")),
            new LogEventProperty("Note", new ScalarValue("safe-note")),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope,
            "private-route", "private-url", "private-alias", "http.operation");
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("safe-note", json.RootElement.GetProperty("Note").GetString());
    }

    [TestMethod]
    public void Literal_restricted_detail_in_message_template_is_removed()
    {
        var source = NewEvent(
            "RequestPayload={\"body\":\"private-template-body\"}", []);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "private-template-body");
    }

    [TestMethod]
    public void Diagnostic_snapshot_rejects_restricted_detail_names_at_every_level()
    {
        var source = NewEvent("diagnostic", [
            new LogEventProperty("ClientIp", new ScalarValue("192.0.2.45")),
            new LogEventProperty("RequestBody", new ScalarValue("body-secret")),
            new LogEventProperty("Metadata", new StructureValue([
                new LogEventProperty("ServerAddress", new ScalarValue("10.0.0.1"))])),
            new LogEventProperty("Fields", new DictionaryValue([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("ResponseBody"), new ScalarValue("response-secret"))])),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "192.0.2.45", "body-secret", "10.0.0.1", "response-secret");
    }

    [TestMethod]
    public void Oversized_template_preserves_validated_event_identity()
    {
        var eventId = Guid.NewGuid().ToString("D");
        var source = NewEvent(new string('x', 2049), [
            new LogEventProperty("LogEventId", new ScalarValue(eventId)),
            new LogEventProperty("RequestBody", new ScalarValue("private-body")),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        Assert.AreEqual(eventId, envelope.LogEventId);
        AssertEnvelopeOmits(envelope, "private-body");
    }

    [TestMethod]
    public void Excessive_template_tokens_preserve_validated_event_identity()
    {
        var eventId = Guid.NewGuid().ToString("D");
        var source = NewEvent(string.Concat(Enumerable.Range(0, 65)
            .Select(index => $"{{Field{index}}}")), [
            new LogEventProperty("LogEventId", new ScalarValue(eventId)),
        ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope));
        Assert.IsNotNull(envelope);
        Assert.AreEqual(eventId, envelope.LogEventId);
    }

    [TestMethod]
    public void Snapshot_is_valid_bounded_utf8_and_does_not_render_raw_exception()
    {
        var source = NewEvent(
            "message {Text}",
            [new LogEventProperty("Text", new ScalarValue(new string('中', 4_096)))],
            new InvalidOperationException("secret-exception-message"));

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source,
            16_384,
            out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        Assert.IsLessThanOrEqualTo(16_384, envelope.Utf8Json.Length);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("message {Text}", json.RootElement.GetProperty("@mt").GetString());
        Assert.IsLessThanOrEqualTo(
            2048,
            json.RootElement.GetProperty("Text").GetString()!.Length);
        Assert.AreEqual(
            nameof(InvalidOperationException),
            json.RootElement.GetProperty("ExceptionType").GetString());
        Assert.AreEqual(
            nameof(InvalidOperationException),
            json.RootElement.GetProperty("@x").GetString());
        Assert.IsFalse(envelope.Utf8Json.Span.IndexOf("secret-exception-message"u8) >= 0);
        Assert.IsNotNull(envelope.LegacyEvent);
        Assert.AreNotSame(source.Exception, envelope.LegacyEvent.Exception);
        Assert.IsFalse(envelope.LegacyEvent.Exception!.ToString()
            .Contains("secret-exception-message", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Sensitive_fields_and_embedded_credentials_do_not_reach_either_output()
    {
        var source = NewEvent(
            "Authorization: Bearer literal-secret {Note}",
            [
                new LogEventProperty("Password", new ScalarValue("field-secret")),
                new LogEventProperty("Note", new ScalarValue("accepted Bearer embedded-secret")),
                new LogEventProperty("Metadata", new StructureValue(
                    [new LogEventProperty("ClientSecret", new ScalarValue("nested-secret"))],
                    "Bearer tag-secret")),
                new LogEventProperty("Headers", new DictionaryValue(
                    [new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        new ScalarValue("X-Api-Key"), new ScalarValue("dictionary-secret"))])),
                new LogEventProperty("token=name-secret", new ScalarValue("unused")),
            ],
            new InvalidOperationException("exception-secret"));

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 16_384, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        var consoleJson = System.Text.Encoding.UTF8.GetString(envelope.Utf8Json.Span);
        using var writer = new StringWriter();
        new CompactJsonFormatter().Format(envelope.LegacyEvent!, writer);
        var legacyJson = writer.ToString();

        foreach (var output in new[] { consoleJson, legacyJson })
        {
            Assert.IsFalse(output.Contains("literal-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("field-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("embedded-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("nested-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("dictionary-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("name-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("tag-secret", StringComparison.Ordinal));
            Assert.IsFalse(output.Contains("exception-secret", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Sensitive_template_drops_opaque_generic_placeholder_value()
    {
        var source = NewEvent(
            "Authorization: {Value}",
            [new LogEventProperty("Value", new ScalarValue("opaque-secret"))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "opaque-secret");
    }

    [TestMethod]
    public void Spaced_sensitive_keys_and_raw_nonce_are_removed()
    {
        var source = NewEvent(
            "properties",
            [
                new LogEventProperty("Private Key", new ScalarValue("private-secret")),
                new LogEventProperty("Connection String", new ScalarValue("connection-secret")),
                new LogEventProperty("Nonce", new ScalarValue("nonce-secret")),
                new LogEventProperty("Authorization=key-secret", new ScalarValue("unused")),
                new LogEventProperty("X-Sign", new ScalarValue("signature-secret")),
                new LogEventProperty("SigningKey", new ScalarValue("signing-secret")),
                new LogEventProperty("DesignId", new ScalarValue("design-safe")),
                new LogEventProperty("AssignmentId", new ScalarValue("assignment-safe")),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope,
            "private-secret", "connection-secret", "nonce-secret", "key-secret",
            "signature-secret", "signing-secret");
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(envelope.Utf8Json.Span)
            .Contains("design-safe", StringComparison.Ordinal));
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(envelope.Utf8Json.Span)
            .Contains("assignment-safe", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Embedded_json_credentials_are_removed_from_generic_string()
    {
        var source = NewEvent(
            "payload {Note}",
            [new LogEventProperty("Note", new ScalarValue(
                "{\"password\":\"json-secret\"}"))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "json-secret");
    }

    [TestMethod]
    public void Unicode_escaped_json_credential_key_is_removed_from_generic_string()
    {
        var source = NewEvent(
            "payload {Note}",
            [new LogEventProperty("Note", new ScalarValue(
                "{\"pass\\u0077ord\":\"escaped-json-secret\"}"))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "escaped-json-secret");
    }

    [TestMethod]
    public void Windows_path_is_not_treated_as_unicode_escaped_assignment()
    {
        const string path = @"C:\Users\alice\file.txt";
        var source = NewEvent(
            "file {Path}",
            [new LogEventProperty("Path", new ScalarValue(path))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        Assert.AreEqual(path, ((ScalarValue)envelope.LegacyEvent!.Properties["Path"]).Value);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual(path, json.RootElement.GetProperty("Path").GetString());
    }

    [TestMethod]
    public void Dotted_credential_assignments_are_removed_from_generic_text()
    {
        var source = NewEvent(
            "payload {Note}",
            [new LogEventProperty("Note", new ScalarValue(
                "private.key=private-secret; connection.string=connection-secret; api.key=api-secret"))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "private-secret", "connection-secret", "api-secret");
    }

    [TestMethod]
    public void Indexed_credential_assignment_is_removed_from_generic_text()
    {
        var source = NewEvent(
            "payload {Note}",
            [new LogEventProperty("Note", new ScalarValue("password[0]=indexed-secret"))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "indexed-secret");
    }

    [TestMethod]
    public void Sensitive_template_never_retains_a_referenced_metadata_placeholder()
    {
        var source = NewEvent(
            "Authorization: {TraceId}",
            [
                new LogEventProperty("TraceId", new ScalarValue("abcdef0123456789abcdef0123456789")),
                new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "abcdef0123456789abcdef0123456789");
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual(LogClassification.Diagnostic,
            json.RootElement.GetProperty("log.class").GetString());
    }

    [TestMethod]
    public void Sign_in_and_out_metrics_are_not_treated_as_signing_keys()
    {
        var source = NewEvent(
            "SignInCount: {SignInCount}, SignOutTime: {SignOutTime}",
            [
                new LogEventProperty("SignInCount", new ScalarValue(5)),
                new LogEventProperty("SignOutTime", new ScalarValue("12:30")),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("SignInCount: {SignInCount}, SignOutTime: {SignOutTime}",
            json.RootElement.GetProperty("@mt").GetString());
        Assert.AreEqual(5, json.RootElement.GetProperty("SignInCount").GetInt32());
    }

    [TestMethod]
    public void Sensitive_template_keeps_only_validated_routing_and_correlation_metadata()
    {
        var source = NewEvent(
            "Authorization: {Value}",
            [
                new LogEventProperty("Value", new ScalarValue("opaque-secret")),
                new LogEventProperty("LogEventId", new ScalarValue(Guid.NewGuid().ToString("D"))),
                new LogEventProperty("log.class", new ScalarValue(LogClassification.HttpOperation)),
                new LogEventProperty("log.stream", new ScalarValue("http-operation")),
                new LogEventProperty("reliability.class", new ScalarValue("Priority")),
                new LogEventProperty("TraceId", new ScalarValue("0123456789abcdef0123456789abcdef")),
                new LogEventProperty("http.status_code", new ScalarValue(200)),
                new LogEventProperty("DataClassification", new ScalarValue("opaque-secret")),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 4096, out var envelope, retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        AssertEnvelopeOmits(envelope, "opaque-secret");
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual(LogClassification.Diagnostic,
            json.RootElement.GetProperty("log.class").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("log.stream", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("reliability.class", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("TraceId", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("http.status_code", out _));
        Assert.IsFalse(json.RootElement.TryGetProperty("DataClassification", out _));
    }

    [TestMethod]
    public void Large_diagnostic_property_is_removed_before_rejecting_essential_event()
    {
        var source = NewEvent(
            "small",
            [
                new LogEventProperty("LogEventId", new ScalarValue("event-123")),
                new LogEventProperty("DiagnosticPayload", new ScalarValue(new string('x', 10_000))),
            ]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 256, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("event-123", json.RootElement.GetProperty("LogEventId").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("DiagnosticPayload", out _));
    }

    [TestMethod]
    public void Unknown_scalar_never_calls_business_to_string()
    {
        var sentinel = new ThrowingScalar();
        var source = NewEvent(
            "unknown {Value}",
            [new LogEventProperty("Value", new ScalarValue(sentinel))]);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source,
            1024,
            out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("[unsupported scalar]", json.RootElement.GetProperty("Value").GetString());
        Assert.AreEqual(0, sentinel.Calls);
        Assert.AreEqual(
            "[unsupported scalar]",
            ((ScalarValue)envelope.LegacyEvent!.Properties["Value"]).Value);
    }

    [TestMethod]
    public void Event_that_cannot_fit_essential_fields_is_rejected()
    {
        var source = NewEvent(
            "important",
            [new LogEventProperty("LogEventId", new ScalarValue("event-123"))]);

        Assert.IsFalse(LogEnvelopeBuilder.TryBuild(source, 16, out var envelope));
        Assert.IsNull(envelope);
    }

    [TestMethod]
    public void Legacy_safe_event_preserves_message_and_properties()
    {
        var source = NewEvent(
            "hello {Name}",
            [
                new LogEventProperty("Name", new ScalarValue("world")),
                new LogEventProperty("LogEventId", new ScalarValue("event-123")),
            ]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source,
            1024,
            out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);

        var restored = envelope.LegacyEvent;
        Assert.IsNotNull(restored);

        Assert.AreEqual("hello \"world\"", restored.RenderMessage());
        Assert.AreEqual(
            "event-123",
            ((ScalarValue)restored.Properties["LogEventId"]).Value);
        Assert.IsNull(restored.Exception);
    }

    [TestMethod]
    public void Property_flood_preserves_generated_identity_before_optional_fields()
    {
        var properties = Enumerable.Range(0, 100)
            .Select(index => new LogEventProperty(
                $"Diagnostic{index}",
                new ScalarValue(index)))
            .Append(new LogEventProperty(
                "LogEventId",
                new ScalarValue("event-123")));
        var source = NewEvent("flood", properties);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 16_384, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("event-123", json.RootElement.GetProperty("LogEventId").GetString());
        Assert.IsLessThanOrEqualTo(66, json.RootElement.EnumerateObject().Count());
    }

    [TestMethod]
    public void Rejected_property_flood_keeps_late_identity_without_scanning_late_optionals()
    {
        var properties = Enumerable.Range(0, 500)
            .Select(index => new LogEventProperty(
                new string('x', 129) + index,
                new ScalarValue("ignored")))
            .Append(new LogEventProperty("LogEventId", new ScalarValue("event-123")))
            .Append(new LogEventProperty("LateOptional", new ScalarValue("late-value")));
        var source = NewEvent("flood", properties);

        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 4096, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("event-123", json.RootElement.GetProperty("LogEventId").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("LateOptional", out _));
    }

    [TestMethod]
    public void Legacy_adapter_preserves_formatted_date_and_dictionary_semantics()
    {
        var timestamp = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var source = NewEvent(
            "at {Time:yyyy} with {Metadata}",
            [
                new LogEventProperty("Time", new ScalarValue(timestamp)),
                new LogEventProperty("Metadata", new DictionaryValue(
                [new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("code"),
                    new ScalarValue(42))])),
            ]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source,
            4096,
            out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);

        var restored = envelope.LegacyEvent;
        Assert.IsNotNull(restored);

        Assert.AreEqual(source.RenderMessage(), restored.RenderMessage());
        Assert.IsInstanceOfType<DictionaryValue>(restored.Properties["Metadata"]);
        Assert.IsInstanceOfType<DateTimeOffset>(((ScalarValue)restored.Properties["Time"]).Value);
    }

    [TestMethod]
    public void Enum_scalar_remains_bounded_and_keeps_legacy_message_value()
    {
        var source = NewEvent(
            "state {State}",
            [new LogEventProperty("State", new ScalarValue(TestState.Degraded))]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source,
            1024,
            out var envelope,
            retainLegacyEvent: true));
        Assert.IsNotNull(envelope);

        var restored = envelope.LegacyEvent;
        Assert.IsNotNull(restored);

        Assert.AreEqual(source.RenderMessage(), restored.RenderMessage());
        Assert.AreEqual(
            TestState.Degraded,
            ((ScalarValue)restored.Properties["State"]).Value);
        using var json = JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual("Degraded", json.RootElement.GetProperty("State").GetString());
    }

    private static LogEvent NewEvent(
        string template,
        IEnumerable<LogEventProperty> properties,
        Exception? exception = null) =>
        new(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception,
            Parser.Parse(template),
            properties);

    private static void AssertEnvelopeOmits(LogEnvelope envelope, params string[] secrets)
    {
        var console = System.Text.Encoding.UTF8.GetString(envelope.Utf8Json.Span);
        using var writer = new StringWriter();
        new CompactJsonFormatter().Format(envelope.LegacyEvent!, writer);
        var legacy = writer.ToString();
        foreach (var secret in secrets)
        {
            Assert.IsFalse(console.Contains(secret, StringComparison.Ordinal));
            Assert.IsFalse(legacy.Contains(secret, StringComparison.Ordinal));
        }
    }

    private sealed class ThrowingScalar
    {
        public int Calls { get; private set; }

        public override string ToString()
        {
            Calls++;
            throw new InvalidOperationException("must not call business ToString");
        }
    }

    private enum TestState
    {
        Degraded = 2,
    }
}
