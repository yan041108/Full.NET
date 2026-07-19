namespace Full.NET.Abstractions.Messaging;

/// <summary>
/// 统一 Integration 事件路由匹配与启动期路由唯一性校验。
/// </summary>
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

    public static void ValidateUniqueRoutes(IEnumerable<IIntegrationEventHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var routeOwners = new Dictionary<(string Type, int SchemaVersion), string>(
            StringTupleComparer.Ordinal);
        foreach (var handler in handlers)
        {
            foreach (var eventType in EnumerateEventTypes(handler))
            {
                var route = (eventType, handler.SchemaVersion);
                if (routeOwners.TryGetValue(route, out var existingOwner)
                    && !string.Equals(
                        existingOwner,
                        handler.GetType().FullName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Integration event route '{eventType}' schema {handler.SchemaVersion} "
                        + $"is registered by both '{existingOwner}' and '{handler.GetType().FullName}'.");
                }

                routeOwners[route] = handler.GetType().FullName ?? handler.GetType().Name;
            }
        }
    }

    private static bool MatchesEventType(
        IIntegrationEventHandler handler,
        string messageType) =>
        string.Equals(handler.EventType, messageType, StringComparison.Ordinal)
        || handler.LegacyEventTypes.Contains(messageType, StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateEventTypes(IIntegrationEventHandler handler)
    {
        yield return handler.EventType;
        foreach (var legacyType in handler.LegacyEventTypes)
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
