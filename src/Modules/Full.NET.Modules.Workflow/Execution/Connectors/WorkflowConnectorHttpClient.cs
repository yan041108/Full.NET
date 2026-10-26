using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>只执行已绑定连接器的一次请求；租约重领不能替代稳定业务幂等键或未知结果对账。</summary>
internal sealed class WorkflowConnectorHttpClient(Func<WorkflowBoundHttpConnector, HttpClient> createClient,
    Func<string, string?> resolveSecret)
{
    public async Task<WorkflowConnectorCallResult> SendAsync(WorkflowBoundHttpConnector connector,
        Guid activationId, string inputJson, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (activationId == Guid.Empty || Encoding.UTF8.GetByteCount(inputJson) > 131072 ||
            !ValidBody(inputJson, connector.Definition.RequestSchema, allowUnknown: false))
            return Rejected("workflow.connector.input_invalid");
        string? secret = null;
        if (connector.Definition.BearerSecretReference is { } reference)
        {
            secret = resolveSecret(reference);
            if (string.IsNullOrEmpty(secret) || secret.Length > 8192 || secret.Any(value => value <= 32 || value >= 127))
                return Rejected("workflow.connector.secret_unavailable");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, connector.Definition.Target);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", activationId.ToString("D"));
        if (secret is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = new StringContent(inputJson, Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(connector.Definition.TimeoutSeconds));
        try
        {
            using var client = createClient(connector);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400) return new("outcome_unknown", status, null, "workflow.connector.redirect_refused");
            if (status is 408 or 429 || status >= 500)
                return new(connector.Definition.SupportsIdempotency ? "retryable" : "outcome_unknown", status, null, "workflow.connector.remote_failure");
            if (!response.IsSuccessStatusCode) return new("rejected", status, null, "workflow.connector.remote_rejected");
            if (response.Content.Headers.ContentLength > WorkflowConnectorPolicy.MaximumResponseBytes)
                return new("failed", status, null, "workflow.connector.response_too_large");
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var buffer = new byte[WorkflowConnectorPolicy.MaximumResponseBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await body.ReadAsync(buffer.AsMemory(length), deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length > WorkflowConnectorPolicy.MaximumResponseBytes) return new("failed", status, null, "workflow.connector.response_too_large");
            var json = length == 0 ? "{}" : new UTF8Encoding(false, true).GetString(buffer, 0, length);
            if (!ValidBody(json, connector.Definition.ResponseSchema, allowUnknown: true)) return new("failed", status, null, "workflow.connector.response_invalid");
            return new("succeeded", status, json, null);
        }
        catch (HttpRequestException exception) when (BlockedDestination(exception))
        { return Rejected("workflow.connector.destination_denied"); }
        catch (WorkflowConnectorDestinationException)
        { return Rejected("workflow.connector.destination_denied"); }
        catch (DecoderFallbackException)
        { return new("failed", null, null, "workflow.connector.response_invalid"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // 发送后失联不能证明远端未生效；仅明确支持幂等的固定版本允许稳定键重试。
            return new(connector.Definition.SupportsIdempotency ? "retryable" : "outcome_unknown", null, null,
                "workflow.connector.result_unconfirmed");
        }
    }

    public static HttpClient CreateProtectedClient(WorkflowBoundHttpConnector connector)
    {
        var network = new WorkflowConnectorNetwork((host, token) => Dns.GetHostAddressesAsync(host, token), WorkflowConnectorNetwork.ConnectSocketAsync);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Min(10, connector.Definition.TimeoutSeconds)),
            ConnectCallback = (context, token) => network.ConnectAsync(connector.Definition.Target, context.DnsEndPoint, token)
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>命名客户端复用连接池；每个新连接仍按该请求的固定目标重新校验并固定DNS结果。</summary>
    public static HttpMessageHandler CreateProtectedHandler()
    {
        var network = new WorkflowConnectorNetwork((host, token) => Dns.GetHostAddressesAsync(host, token), WorkflowConnectorNetwork.ConnectSocketAsync);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectCallback = (context, token) => network.ConnectAsync(context.InitialRequestMessage.RequestUri
                ?? throw new WorkflowConnectorDestinationException(), context.DnsEndPoint, token)
        };
    }

    private static WorkflowConnectorCallResult Rejected(string code) => new("rejected", null, null, code);
    private static bool ValidBody(string json, WorkflowFormSchema schema, bool allowUnknown)
    {
        try
        {
            using var body = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (body.RootElement.ValueKind != JsonValueKind.Object) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var known = schema.Sections.SelectMany(section => section.Fields).Select(field => field.FieldKey).ToHashSet(StringComparer.Ordinal);
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in body.RootElement.EnumerateObject())
            {
                if (!keys.Add(field.Name) || !allowUnknown && !known.Contains(field.Name)) return false;
                if (known.Contains(field.Name)) values.Add(field.Name, field.Value.Clone());
            }
            return WorkflowFormValueValidator.Validate(schema, values);
        }
        catch (JsonException) { return false; }
    }
    private static bool BlockedDestination(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is WorkflowConnectorDestinationException) return true;
        return false;
    }
}

/// <summary>结果不含凭据或网络异常原文，未知副作用必须由活动状态机保存并挂起对账。</summary>
internal sealed record WorkflowConnectorCallResult(string OutcomeKey, int? HttpStatusCode, string? ResponseJson, string? ErrorKey);
