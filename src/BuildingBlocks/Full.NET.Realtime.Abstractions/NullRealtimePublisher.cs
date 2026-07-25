namespace Full.NET.Realtime;

/// <summary>
/// Realtime 关闭时的空实现，避免业务模块分支判断发布器是否存在。
/// </summary>
public sealed class NullRealtimePublisher : IRealtimePublisher
{
    public static NullRealtimePublisher Instance { get; } = new();

    private NullRealtimePublisher()
    {
    }

    public Task PublishToUserAsync(
        Guid userId,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishToGroupAsync(
        string groupName,
        RealtimeMessage message,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
