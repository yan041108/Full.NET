namespace Full.NET.AI.Providers.Http;

/// <summary>在策略校验之前开启观测；收到响应头即结束，不把 HTTP 成功当成模型或结算成功。</summary>
internal sealed class AiProviderTelemetryHandler(string provider, string operation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var observation = AiProviderTelemetry.Start(provider, operation);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            observation.Outcome = (int)response.StatusCode switch
            {
                >= 200 and < 300 => "succeeded",
                >= 300 and < 400 => "redirected",
                429 => "rate_limited",
                >= 400 and < 500 => "rejected",
                >= 500 and < 600 => "server_error",
                _ => "invalid_status",
            };
            return response;
        }
        catch (OperationCanceledException) { observation.Outcome = "cancelled"; throw; }
        catch (HttpRequestException error)
        {
            observation.Outcome = AiEndpointPolicy.IsRejection(error) ? "policy_rejected" : "network_error";
            throw;
        }
    }
}
