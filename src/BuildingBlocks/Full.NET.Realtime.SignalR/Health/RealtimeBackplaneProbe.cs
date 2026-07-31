using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR.Health;

internal sealed class RealtimeBackplaneProbe : IRealtimeBackplaneProbe
{
    public async Task PingAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        // ready 探针必须快速失败，不能沿用运行连接的后台重连语义拖住实例摘流。
        configuration.AbortOnConnectFail = true;
        configuration.ConnectRetry = 0;
        configuration.ConnectTimeout = 1000;
        configuration.AsyncTimeout = 1000;
        await using var connection = await ConnectionMultiplexer
            .ConnectAsync(configuration)
            .WaitAsync(cancellationToken);
        _ = await connection
            .GetDatabase()
            .PingAsync()
            .WaitAsync(cancellationToken);
    }
}
