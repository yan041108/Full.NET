namespace Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;

/// <summary>按不可变创建键跨轮扫描镜像；未变化的运行中记录不会永久占满首批。</summary>
internal sealed class DingTalkApprovalPollCursor
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    internal Guid? Id { get; set; }
}
