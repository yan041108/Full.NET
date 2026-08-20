using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>创建绑定 SSRF 校验连接回调的 HTTP Handler，确保实际连接地址就是已校验地址。</summary>
internal static class JobsHttpMessageHandlerFactory
{
    public static SocketsHttpHandler Create(IOptions<JobsHttpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                HttpSsrfGuard.ConnectAsync(
                    context.DnsEndPoint,
                    options.Value.AllowPrivateNetwork,
                    cancellationToken),
        };
    }
}
