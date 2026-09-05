using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Features.CreateNotificationIntents;

/// <summary>按可信通知作用域批量校验活动用户，禁止 Tenant 请求退化为全局 Host 用户校验。</summary>
/// <param name="hostUsers">活动 Host 用户批量目录。</param>
/// <param name="tenantUsers">当前可信 Tenant 的活动用户批量目录。</param>
internal sealed class NotificationRecipientDirectoryResolver(
    IHostUserBatchSelectionDirectory hostUsers,
    ITenantUserSelectionDirectory tenantUsers)
{
    /// <summary>按输入顺序解析全部收件人；任一用户不属于当前作用域时失败关闭。</summary>
    /// <param name="scope">由请求上下文或消息 Envelope 构造的可信通知作用域。</param>
    /// <param name="recipients">已规范化且仅包含用户类型的收件人。</param>
    /// <param name="cancellationToken">取消当前目录查询的令牌。</param>
    /// <returns>保持输入顺序的活动用户标识，或精确的收件人不存在错误。</returns>
    public async Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        NotificationInboxScope scope,
        IReadOnlyCollection<NotificationRecipientInput> recipients,
        CancellationToken cancellationToken)
    {
        var userIds = recipients
            .Select(recipient => Guid.Parse(recipient.RecipientKey))
            .ToArray();

        IReadOnlySet<Guid> activeUserIds;
        if (scope.IsHost)
        {
            var directory = await hostUsers
                .FindActiveHostUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            activeUserIds = directory.Keys.ToHashSet();
        }
        else
        {
            var directory = await tenantUsers
                .FindActiveTenantUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            activeUserIds = directory.Keys.ToHashSet();
        }

        // 必须校验完整请求集合，禁止把跨租户、停用或不存在用户静默裁剪后继续投递。
        if (userIds.Any(userId => !activeUserIds.Contains(userId)))
        {
            return Result<IReadOnlyList<Guid>>.Failure(new Error(
                NotificationsErrorCodes.InboxRecipientNotFound,
                "The recipient user was not found.",
                ErrorType.NotFound));
        }

        return Result<IReadOnlyList<Guid>>.Success(userIds);
    }
}
