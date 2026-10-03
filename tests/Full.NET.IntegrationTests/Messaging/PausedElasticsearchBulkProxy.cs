using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>仅供进程故障测试：先将 Bulk 送达真实 ES，再暂扣给 Consumer 的回执。</summary>
internal sealed class PausedElasticsearchBulkProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<(HttpStatusCode Status, byte[] Body)> _forwarded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _run;
    private readonly X509Certificate2 _certificate;
    private readonly HttpClient _upstream;
    private readonly Uri _upstreamBulk;

    public PausedElasticsearchBulkProxy(X509Certificate2 certificate, HttpClient upstream, Uri esUri)
    {
        _certificate = certificate;
        _upstream = upstream;
        _upstreamBulk = new Uri(esUri, "_bulk");
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _run = RunAsync();
    }

    public int Port { get; }

    public Task<(HttpStatusCode Status, byte[] Body)> Forwarded => _forwarded.Task;

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, _stop.Token);

            var header = new List<byte>();
            var one = new byte[1];
            while (header.Count < 16 * 1024)
            {
                await tls.ReadExactlyAsync(one, _stop.Token);
                header.Add(one[0]);
                if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n'
                    && header[^2] == '\r' && header[^1] == '\n') break;
            }
            if (header.Count == 16 * 1024) throw new InvalidDataException("Bulk 请求头超限。");
            var lines = Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.None);
            if (!lines[0].StartsWith("POST /_bulk HTTP/1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("仅允许测试 Bulk 请求。");
            }
            var contentLengthLine = lines.FirstOrDefault(line =>
                line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            if (contentLengthLine is null
                || !int.TryParse(contentLengthLine.AsSpan("Content-Length:".Length).Trim(),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length is < 1 or > 1024 * 1024)
            {
                throw new InvalidDataException("Bulk 请求体长度无效。");
            }
            var body = new byte[length];
            await tls.ReadExactlyAsync(body, _stop.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, _upstreamBulk)
            {
                Content = new ByteArrayContent(body),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
            using var response = await _upstream.SendAsync(request, _stop.Token);
            var responseBody = await response.Content.ReadAsByteArrayAsync(_stop.Token);
            _forwarded.TrySetResult((response.StatusCode, responseBody));
            await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // 故障测试在真实 ES 确认后终止 Consumer，代理随后由测试释放。
        }
        catch (Exception error)
        {
            _forwarded.TrySetException(error);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _run;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        _stop.Dispose();
    }
}
