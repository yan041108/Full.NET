namespace Full.NET.Realtime.SignalR.Health;

internal interface IRealtimeBackplaneProbe
{
    Task PingAsync(
        string connectionString,
        CancellationToken cancellationToken);
}
