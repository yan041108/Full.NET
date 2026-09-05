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
    /// <returns>保持输入顺序的已校验收件人及其偏好语言，或精确的收件人不存在错误。</returns>
    public async Task<Result<IReadOnlyList<ResolvedNotificationRecipient>>> ResolveAsync(
        NotificationInboxScope scope,
        IReadOnlyCollection<NotificationRecipientInput> recipients,
        CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedNotificationRecipient>(recipients.Count);
        if (scope.IsHost)
        {
            var userIds = recipients
                .Select(recipient => Guid.Parse(recipient.RecipientKey))
                .ToArray();
            var directory = await hostUsers
                .FindActiveHostUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            foreach (var recipient in recipients)
            {
                var userId = Guid.Parse(recipient.RecipientKey);
                if (!directory.TryGetValue(userId, out var entry))
                {
                    return Result<IReadOnlyList<ResolvedNotificationRecipient>>.Failure(RecipientNotFound());
                }

                resolved.Add(new ResolvedNotificationRecipient(
                    recipient,
                    userId,
                    entry.PreferredLocale));
            }
        }
        else
        {
            var userIds = recipients
                .Select(recipient => Guid.Parse(recipient.RecipientKey))
                .ToArray();
            var directory = await tenantUsers
                .FindActiveTenantUsersAsync(userIds, cancellationToken)
                .ConfigureAwait(false);
            foreach (var recipient in recipients)
            {
                var userId = Guid.Parse(recipient.RecipientKey);
                if (!directory.TryGetValue(userId, out var entry))
                {
                    return Result<IReadOnlyList<ResolvedNotificationRecipient>>.Failure(RecipientNotFound());
                }

                resolved.Add(new ResolvedNotificationRecipient(
                    recipient,
                    userId,
                    entry.PreferredLocale));
            }
        }

        return Result<IReadOnlyList<ResolvedNotificationRecipient>>.Success(resolved);
    }

    private static Error RecipientNotFound() =>
        new(
            NotificationsErrorCodes.InboxRecipientNotFound,
            "The recipient user was not found.",
            ErrorType.NotFound);
}

/// <summary>已校验的收件人及其偏好语言，供模板选择与 Inbox 投影使用。</summary>
/// <param name="Input">规范化后的收件人输入。</param>
/// <param name="UserId">活动用户标识。</param>
/// <param name="PreferredLocale">规范 BCP 47 偏好语言。</param>
internal sealed record ResolvedNotificationRecipient(
    NotificationRecipientInput Input,
    Guid UserId,
    string PreferredLocale);
