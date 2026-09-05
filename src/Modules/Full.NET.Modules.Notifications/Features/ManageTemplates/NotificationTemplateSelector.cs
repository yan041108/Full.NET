using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManageTemplates;

/// <summary>按模板键与偏好语言在已发布变体中解析模板行及其最新版本。</summary>
/// <param name="queryExecutor">受治理的只读查询执行器。</param>
internal sealed class NotificationTemplateSelector(IQueryExecutor queryExecutor)
{
    /// <summary>在已发布语言变体中按偏好、别名链与默认语言挑选模板并加载版本。</summary>
    /// <param name="scope">可信通知作用域。</param>
    /// <param name="templateKey">稳定模板键。</param>
    /// <param name="preferredLocaleTag">收件人或调用方偏好语言。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>已发布模板与版本；不存在或未发布时返回精确业务错误。</returns>
    public async Task<Result<SelectedNotificationTemplate>> ResolvePublishedAsync(
        NotificationInboxScope scope,
        string templateKey,
        string preferredLocaleTag,
        CancellationToken cancellationToken)
    {
        var localeStates = (await queryExecutor.QueryAsync<NotificationTemplateLocaleStateRecord>(
                    NotificationPlatformSql.ListTemplateLocalesByKey,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("TemplateKey", templateKey)),
                    cancellationToken)
                .ConfigureAwait(false))
            .ToArray();
        if (localeStates.Length == 0)
        {
            return Result<SelectedNotificationTemplate>.Failure(TemplateNotFound());
        }

        var publishedTags = localeStates
            .Where(state => state.LatestPublishedVersionId is not null)
            .Select(state => state.LocaleTag)
            .ToArray();
        if (publishedTags.Length == 0)
        {
            return Result<SelectedNotificationTemplate>.Failure(TemplateNotPublished());
        }

        var normalizedPreferred = NotificationTemplateLocaleResolver.NormalizeLocaleTag(preferredLocaleTag);
        if (!normalizedPreferred.IsSuccess)
        {
            return Result<SelectedNotificationTemplate>.Failure(normalizedPreferred.Error!);
        }

        var defaultLocaleTag = localeStates[0].DefaultLocaleTag;
        var pickedLocale = NotificationTemplateLocaleResolver.PickPublishedLocale(
            publishedTags,
            normalizedPreferred.Value!,
            defaultLocaleTag);
        if (pickedLocale is null)
        {
            return Result<SelectedNotificationTemplate>.Failure(TemplateNotPublished());
        }

        var template = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
                NotificationPlatformSql.FindTemplateByKeyAndLocale,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("TemplateKey", templateKey),
                    ("LocaleTag", pickedLocale)),
                cancellationToken)
            .ConfigureAwait(false);
        if (template?.LatestPublishedVersionId is not { } versionId)
        {
            return Result<SelectedNotificationTemplate>.Failure(TemplateNotPublished());
        }

        var version = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
                NotificationPlatformSql.FindTemplateVersionById,
                NotificationPlatformSqlParameters.Create(("Id", versionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return version is null
            ? Result<SelectedNotificationTemplate>.Failure(TemplateNotPublished())
            : Result<SelectedNotificationTemplate>.Success(new SelectedNotificationTemplate(template, version));
    }

    private static Error TemplateNotFound() =>
        new(
            NotificationsErrorCodes.TemplateNotFound,
            "The notification template was not found.",
            ErrorType.NotFound);

    private static Error TemplateNotPublished() =>
        new(
            NotificationsErrorCodes.TemplateNotPublished,
            "The notification template has not been published.",
            ErrorType.BusinessRule);
}

/// <summary>已发布模板行与其不可变版本快照。</summary>
/// <param name="Template">匹配到的语言变体模板行。</param>
/// <param name="Version">该变体当前最新发布版本。</param>
internal sealed record SelectedNotificationTemplate(
    NotificationTemplateRecord Template,
    NotificationTemplateVersionRecord Version);
