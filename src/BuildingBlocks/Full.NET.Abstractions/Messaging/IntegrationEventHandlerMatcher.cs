namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 旧 Outbox 轮询路径的 Integration 事件路由匹配与启动期唯一性校验。
/// </summary>
/// <remarks>
/// 此处按 (MessageType, SchemaVersion) 保证旧 Worker 内路由唯一，并展开
/// <see cref="IIntegrationEventHandler.LegacyEventTypes"/> 别名。Kafka 订阅目录使用
/// (ConsumerName, EventType, SchemaVersion) 路由键，不得削弱本类对旧轮询所有者的约束。
/// </remarks>
public static class IntegrationEventHandlerMatcher
{
    public static IReadOnlyList<IIntegrationEventHandler> Match(
        IReadOnlyCollection<IIntegrationEventHandler> handlers,
        string messageType,
        int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        return handlers
            .Where(handler => handler.SchemaVersion == schemaVersion
                && MatchesEventType(handler, messageType))
            .ToArray();
    }

    /// <summary>
    /// 校验旧轮询 Handler 在 (MessageType, SchemaVersion) 维度上的路由唯一性。
    /// </summary>
    public static void ValidateUniqueRoutes(IEnumerable<IIntegrationEventHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var routeOwners = new Dictionary<(string Type, int SchemaVersion), string>(
            StringTupleComparer.Ordinal);
        foreach (var handler in handlers)
        {
            var owner = handler.GetType().FullName ?? handler.GetType().Name;
            var eventType = handler.EventType;
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new InvalidOperationException(
                    $"Integration event handler '{owner}' must declare a non-empty "
                    + $"{nameof(IIntegrationEventHandler.EventType)}.");
            }

            var schemaVersion = handler.SchemaVersion;
            if (schemaVersion < 1)
            {
                throw new InvalidOperationException(
                    $"Integration event handler '{owner}' must declare a positive "
                    + $"{nameof(IIntegrationEventHandler.SchemaVersion)}.");
            }

            var idempotencyStrategy = handler.IdempotencyStrategy;
            if (idempotencyStrategy is not (
                IntegrationEventIdempotencyStrategy.NaturallyIdempotent
                or IntegrationEventIdempotencyStrategy.MessageIdDeduplication))
            {
                throw new InvalidOperationException(
                    $"Integration event handler '{owner}' must declare a supported "
                    + $"{nameof(IIntegrationEventHandler.IdempotencyStrategy)}.");
            }

            var legacyEventTypes = handler.LegacyEventTypes;
            if (legacyEventTypes.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"Integration event handler '{owner}' must not declare empty "
                    + $"{nameof(IIntegrationEventHandler.LegacyEventTypes)} entries.");
            }

            foreach (var routeEventType in EnumerateEventTypes(
                eventType,
                legacyEventTypes))
            {
                var route = (routeEventType, schemaVersion);
                if (routeOwners.TryGetValue(route, out var existingOwner))
                {
                    throw new InvalidOperationException(
                        $"Integration event route '{routeEventType}' schema {schemaVersion} "
                        + $"is registered by both '{existingOwner}' and '{owner}'.");
                }

                routeOwners[route] = owner;
            }
        }
    }

    private static bool MatchesEventType(
        IIntegrationEventHandler handler,
        string messageType) =>
        string.Equals(handler.EventType, messageType, StringComparison.Ordinal)
        || handler.LegacyEventTypes.Contains(messageType, StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateEventTypes(
        string eventType,
        IReadOnlyList<string> legacyEventTypes)
    {
        yield return eventType;
        foreach (var legacyType in legacyEventTypes)
        {
            yield return legacyType;
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Type, int SchemaVersion)>
    {
        public static StringTupleComparer Ordinal { get; } = new();

        public bool Equals((string Type, int SchemaVersion) x, (string Type, int SchemaVersion) y) =>
            x.SchemaVersion == y.SchemaVersion
            && string.Equals(x.Type, y.Type, StringComparison.Ordinal);

        public int GetHashCode((string Type, int SchemaVersion) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Type), obj.SchemaVersion);
    }
}
