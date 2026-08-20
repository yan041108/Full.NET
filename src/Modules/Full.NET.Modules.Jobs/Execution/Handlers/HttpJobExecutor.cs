using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution.Handlers;

/// <summary>可配置 HTTP 任务执行器；解析 Args、SSRF 校验后发起无 Body 请求。</summary>
internal sealed class HttpJobExecutor(
    IHttpClientFactory httpClientFactory,
    ISettingsSecretValueResolver secretValueResolver,
    IOptions<JobsHttpOptions> httpOptions) : IJobHandlerExecutor
{
    public const string HttpClientName = "Jobs.Http";

    private const int MaxErrorBodyBytes = 2048;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string HandlerKind => JobHandlerKinds.Http;

    public async Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var args = DeserializeArgs(context.ArgsJson);
        if (!HttpJobArgsValidator.TryValidate(args, rejectSensitivePlainHeaders: true, out _))
        {
            throw new InvalidOperationException("HTTP job args are invalid.");
        }

        if (!Uri.TryCreate(args.Url.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("HTTP job URL is invalid.");
        }

        var allowPrivate = httpOptions.Value.AllowPrivateNetwork;
        var (allowed, reason) = await HttpSsrfGuard
            .ValidateAsync(uri, allowPrivate, cancellationToken)
            .ConfigureAwait(false);
        if (!allowed)
        {
            throw new InvalidOperationException(reason ?? "HTTP job URL is blocked.");
        }

        using var request = new HttpRequestMessage(
            new HttpMethod(args.Method.Trim().ToUpperInvariant()),
            uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "Full.NET-Jobs/1.0");

        if (args.Headers is not null)
        {
            foreach (var (name, value) in args.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (args.SecretHeaders is not null)
        {
            foreach (var (name, reference) in args.SecretHeaders)
            {
                var resolved = await secretValueResolver
                    .ResolveSecretValueAsync(reference.ConfigKey, cancellationToken)
                    .ConfigureAwait(false);
                if (!resolved.IsSuccess || string.IsNullOrEmpty(resolved.Value))
                {
                    throw new InvalidOperationException(
                        "HTTP job secret header could not be resolved.");
                }

                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, resolved.Value);
            }
        }

        var timeoutSeconds = args.TimeoutSeconds ?? 30;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token)
            .ConfigureAwait(false);

        var successCodes = args.SuccessStatusCodes ?? [200, 201, 202, 204];
        if (successCodes.Contains((int)response.StatusCode))
        {
            return;
        }

        var summary = await BuildFailureSummaryAsync(
                request.Method.Method,
                uri,
                response,
                cancellationToken)
            .ConfigureAwait(false);
        throw new InvalidOperationException(summary);
    }

    private static HttpJobArgs DeserializeArgs(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            throw new InvalidOperationException("HTTP job args are required.");
        }

        return JsonSerializer.Deserialize<HttpJobArgs>(argsJson, SerializerOptions)
            ?? throw new InvalidOperationException("HTTP job args are invalid.");
    }

    private static async Task<string> BuildFailureSummaryAsync(
        string method,
        Uri uri,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string reason = response.ReasonPhrase ?? string.Empty;
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var buffer = new byte[MaxErrorBodyBytes];
            var read = await stream
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read > 0)
            {
                reason = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 错误摘要仅使用状态行；读取响应体失败时保留 reason phrase。
        }

        var hostPath = $"{uri.Host}{uri.AbsolutePath}";
        var trimmedReason = reason.Length > 256 ? reason[..256] : reason;
        return $"{method} {hostPath} failed with {(int)response.StatusCode}: {trimmedReason}";
    }
}
