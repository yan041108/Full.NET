using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManagePreferences;

/// <summary>管理本人通知发送偏好；收件端点由 RecipientEndpointStore 独立维护。</summary>
internal sealed class NotificationPreferenceService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    Providers.INotificationProviderTypeCatalog providerTypeCatalog)
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceResponse>>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<NotificationPreferenceRecord>(
                NotificationPlatformSql.ListPreferencesByUser,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<NotificationPreferenceResponse>>.Success(rows.Select(Map).ToArray());
    }

    public Task<Result<NotificationPreferenceResponse>> UpdateMineAsync(
        Guid userId,
        UpdateMyNotificationPreferenceRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(userId, request, token),
            cancellationToken);

    private async Task<Result<NotificationPreferenceResponse>> UpdateCoreAsync(
        Guid userId,
        UpdateMyNotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var channel = request.ChannelKey?.Trim() ?? string.Empty;
        // 偏好渠道与模板、目录共用同一闭合来源：内建 inbox 或已注册 Adapter 真实支持的渠道。
        // 收件端点管理是独立能力，这里只登记发送意愿，不代表端点存在或授权可用。
        if (!Domain.NotificationTemplateCompiler.NormalizeChannel(channel, providerTypeCatalog).IsSuccess)
        {
            return Result<NotificationPreferenceResponse>.Failure(ValidationError());
        }

        var quietHours = NormalizeQuietHours(request.QuietHoursJson);
        if (!quietHours.IsSuccess)
        {
            return Result<NotificationPreferenceResponse>.Failure(quietHours.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(
                NotificationPlatformSql.FindPreferenceByUserChannel,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("ChannelKey", channel)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertPreference,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("UserId", userId),
                        ("ChannelKey", channel),
                        ("ChannelOptedOut", request.ChannelOptedOut),
                        ("MarketingConsentGranted", request.MarketingConsentGranted),
                        ("QuietHoursJson", quietHours.Value),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var updated = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.UpdatePreference,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", existing.Id),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("UserId", userId),
                        ("Version", existing.Version),
                        ("ChannelOptedOut", request.ChannelOptedOut),
                        ("MarketingConsentGranted", request.MarketingConsentGranted),
                        ("QuietHoursJson", quietHours.Value),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated == 0)
            {
                return Result<NotificationPreferenceResponse>.Failure(new Error(
                    NotificationsErrorCodes.BindingConcurrencyConflict,
                    "The notification preference changed concurrently.",
                    ErrorType.Conflict));
            }
        }

        var saved = await queryExecutor.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(
                NotificationPlatformSql.FindPreferenceByUserChannel,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("ChannelKey", channel)),
                cancellationToken)
            .ConfigureAwait(false);
        return saved is null
            ? Result<NotificationPreferenceResponse>.Failure(ValidationError())
            : Result<NotificationPreferenceResponse>.Success(Map(saved));
    }

    internal static Result<string?> NormalizeQuietHours(string? quietHoursJson)
    {
        if (string.IsNullOrWhiteSpace(quietHoursJson))
        {
            return Result<string?>.Success(null);
        }

        try
        {
            using var document = JsonDocument.Parse(quietHoursJson);
            var root = document.RootElement;
            var start = root.GetProperty("start").GetString();
            var end = root.GetProperty("end").GetString();
            var timeZone = root.GetProperty("timeZone").GetString();
            if (!IsHourMinute(start) || !IsHourMinute(end) || start == end
                || string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 64)
            {
                return Result<string?>.Failure(ValidationError());
            }

            // 时区必须能被实际发送端解析，否则声明会被保存却失去静默保护。
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("start", start);
                writer.WriteString("end", end);
                writer.WriteString("timeZone", timeZone);
                writer.WriteEndObject();
            }

            return Result<string?>.Success(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException)
        {
            return Result<string?>.Failure(ValidationError());
        }
        catch (KeyNotFoundException)
        {
            return Result<string?>.Failure(ValidationError());
        }
        catch (InvalidOperationException)
        {
            return Result<string?>.Failure(ValidationError());
        }
        catch (TimeZoneNotFoundException)
        {
            return Result<string?>.Failure(ValidationError());
        }
        catch (InvalidTimeZoneException)
        {
            return Result<string?>.Failure(ValidationError());
        }
    }

    private static bool IsHourMinute(string? value) =>
        value is { Length: 5 }
        && int.TryParse(value.AsSpan(0, 2), out var hour)
        && value[2] == ':'
        && int.TryParse(value.AsSpan(3, 2), out var minute)
        && hour is >= 0 and <= 23
        && minute is >= 0 and <= 59;

    private static NotificationPreferenceResponse Map(NotificationPreferenceRecord record) =>
        new(record.Id, record.UserId, record.ChannelKey, record.ChannelOptedOut,
            record.MarketingConsentGranted, record.QuietHoursJson, record.UpdatedAtUtc, record.Version);

    private static Error ValidationError() => new(
        NotificationsErrorCodes.PreferenceValidationFailed,
        "The notification preference is invalid.",
        ErrorType.Validation);
}
