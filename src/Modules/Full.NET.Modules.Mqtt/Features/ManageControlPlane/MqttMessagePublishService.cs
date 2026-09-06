using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Mqtt.Configuration;
using Full.NET.Modules.Mqtt.Contracts;
using Full.NET.Modules.Mqtt.Infrastructure;
using Full.NET.Modules.Mqtt.Persistence;
using Full.NET.Modules.Mqtt.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Mqtt.Features.ManageControlPlane;

/// <summary>受 ACL、载荷、速率与幂等键约束的 MQTT 发布服务。</summary>
internal sealed class MqttMessagePublishService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<MqttBrokerOptions> brokerOptions,
    MqttPublishRateLimiter rateLimiter,
    MqttBrokerPublisher brokerPublisher)
{
    /// <summary>在限额内发布 MQTT 消息并持久化结果。</summary>
    /// <param name="actorUserId">操作人标识。</param>
    /// <param name="request">发布请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息记录或稳定错误。</returns>
    public async Task<Result<MqttMessageResponse>> PublishAsync(
        Guid actorUserId,
        PublishMqttMessageRequest request,
        CancellationToken cancellationToken)
    {
        var broker = brokerOptions.Value;
        if (!broker.Enabled)
        {
            return Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.BrokerUnavailable,
                "MQTT broker publishing is disabled.",
                ErrorType.Conflict));
        }

        var validation = ValidateRequest(request, broker);
        if (validation is not null)
        {
            return validation;
        }

        var topic = request.Topic.Trim();
        var tenantId = currentTenant.IsHost ? null : currentTenant.Id;
        if (!MqttTopicAccessPolicy.CanPublish(
                topic,
                currentTenant.IsHost,
                tenantId,
                broker.AllowedPublishTopicPrefixes))
        {
            return Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.TopicForbidden,
                "The MQTT topic is not allowed for the current scope.",
                ErrorType.Forbidden));
        }

        if (!rateLimiter.TryAcquire(
                actorUserId,
                broker.MaximumPublishRatePerMinute,
                clock.UtcNow))
        {
            return Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.PublishRateLimited,
                "MQTT publish rate limit exceeded.",
                ErrorType.RateLimited));
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();
        if (idempotencyKey is not null)
        {
            var existing = await queryExecutor.QuerySingleOrDefaultAsync<MqttMessageRecord>(
                    MqttSql.FindMessageByIdempotency,
                    MqttSqlParameters.Create(
                        ("TenantId", tenantId),
                        ("IdempotencyKey", idempotencyKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (MatchesIdempotentReplay(existing, request, topic))
                {
                    return Result<MqttMessageResponse>.Success(
                        MqttMessageQueryService.Map(existing));
                }

                return Result<MqttMessageResponse>.Failure(new Error(
                    MqttErrorCodes.IdempotencyConflict,
                    "The idempotency key was already used with different publish parameters.",
                    ErrorType.Conflict));
            }
        }

        if (request.ClientId is Guid clientId)
        {
            var client = await queryExecutor.QuerySingleOrDefaultAsync<MqttClientRecord>(
                    MqttSql.FindClientById,
                    MqttSqlParameters.Create(("Id", clientId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (client is null || !client.IsEnabled)
            {
                return Result<MqttMessageResponse>.Failure(new Error(
                    MqttErrorCodes.ClientNotFound,
                    "The MQTT client was not found.",
                    ErrorType.NotFound));
            }
        }

        var now = clock.UtcNow;
        var messageId = idGenerator.NewId();
        var payloadSize = Encoding.UTF8.GetByteCount(request.Payload);
        await commandExecutor.ExecuteAsync(
                MqttSql.InsertMessage,
                MqttSqlParameters.Create(
                    ("Id", messageId),
                    ("TenantId", tenantId),
                    ("ClientId", request.ClientId),
                    ("Topic", topic),
                    ("PayloadSizeBytes", payloadSize),
                    ("Qos", request.Qos),
                    ("Status", MqttMessageStatuses.Pending),
                    ("IdempotencyKey", idempotencyKey),
                    ("SummaryMessage", "Pending publish."),
                    ("PublishedAtUtc", null),
                    ("CreatedAtUtc", now),
                    ("CreatedByUserId", actorUserId)),
                cancellationToken)
            .ConfigureAwait(false);

        var publishResult = await brokerPublisher.PublishAsync(
                topic,
                request.Payload,
                request.Qos,
                cancellationToken)
            .ConfigureAwait(false);
        var status = publishResult.Succeeded
            ? MqttMessageStatuses.Published
            : MqttMessageStatuses.Failed;
        var summary = publishResult.Succeeded
            ? "Published successfully."
            : publishResult.ErrorMessage ?? "Publish failed.";
        var publishedAtUtc = publishResult.Succeeded ? now : (DateTimeOffset?)null;
        await commandExecutor.ExecuteAsync(
                MqttSql.UpdateMessageStatus,
                MqttSqlParameters.Create(
                    ("Id", messageId),
                    ("Status", status),
                    ("SummaryMessage", summary),
                    ("PublishedAtUtc", publishedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<MqttMessageRecord>(
                MqttSql.FindMessageById,
                MqttSqlParameters.Create(("Id", messageId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.MessageNotFound,
                "The MQTT message was not found.",
                ErrorType.NotFound))
            : Result<MqttMessageResponse>.Success(MqttMessageQueryService.Map(record));
    }

    private static Result<MqttMessageResponse>? ValidateRequest(
        PublishMqttMessageRequest request,
        MqttBrokerOptions broker)
    {
        if (string.IsNullOrWhiteSpace(request.Topic)
            || request.Qos is < 0 or > 2
            || request.Payload is null)
        {
            return ValidationFailed();
        }

        if (Encoding.UTF8.GetByteCount(request.Payload) > broker.MaximumPayloadBytes)
        {
            return Result<MqttMessageResponse>.Failure(new Error(
                MqttErrorCodes.PayloadTooLarge,
                "The MQTT payload exceeds the configured maximum size.",
                ErrorType.Validation));
        }

        if (request.IdempotencyKey is { Length: > 128 })
        {
            return ValidationFailed();
        }

        return null;
    }

    private static bool MatchesIdempotentReplay(
        MqttMessageRecord existing,
        PublishMqttMessageRequest request,
        string topic) =>
        string.Equals(existing.Topic, topic, StringComparison.Ordinal)
        && existing.Qos == request.Qos
        && existing.PayloadSizeBytes == Encoding.UTF8.GetByteCount(request.Payload)
        && existing.ClientId == request.ClientId;

    private static Result<MqttMessageResponse> ValidationFailed() =>
        Result<MqttMessageResponse>.Failure(new Error(
            MqttErrorCodes.PublishValidationFailed,
            "The MQTT publish request failed validation.",
            ErrorType.Validation));
}
