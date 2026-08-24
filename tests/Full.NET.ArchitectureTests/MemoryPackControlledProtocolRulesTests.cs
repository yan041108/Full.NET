using System.Reflection;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using global::MemoryPack;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 验证 Integration Event MemoryPack 载荷遵守 ADR-0008 §4.6 受控二进制协议：
/// 仅具体 DTO、禁止接口/多态/object，并与传输 Envelope 分层。
/// </summary>
[TestClass]
public sealed class MemoryPackControlledProtocolRulesTests
{
    private static readonly Type[] ProductionIntegrationEventTypes =
    [
        typeof(TenantProvisionedIntegrationEvent),
        typeof(TenantChangedIntegrationEvent),
        typeof(AnnouncementPublishedIntegrationEvent),
        typeof(InboxMessageReceivedIntegrationEvent),
        typeof(InboxReadStateChangedIntegrationEvent),
        typeof(IdentityOrganizationUnitChangedIntegrationEvent),
    ];

    private static readonly HashSet<Type> ForbiddenPropertyTypes =
    [
        typeof(object),
        typeof(IEnumerable<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IDictionary<,>),
        typeof(IReadOnlyDictionary<,>),
    ];

    [TestMethod]
    public void ProductionIntegrationEvents_AreConcreteMemoryPackablePartialTypes()
    {
        foreach (var eventType in ProductionIntegrationEventTypes)
        {
            Assert.IsTrue(
                eventType.IsDefined(typeof(MemoryPackableAttribute), inherit: false),
                $"{eventType.FullName} 必须标注 [MemoryPackable]。");
            Assert.IsTrue(
                eventType.IsDefined(typeof(MemoryPackUnionAttribute), inherit: false) is false,
                $"{eventType.FullName} 禁止使用 [MemoryPackUnion] 多态载荷。");
            Assert.IsFalse(
                eventType.IsAbstract,
                $"{eventType.FullName} 不得为 abstract。");
        }
    }

    [TestMethod]
    public void ProductionIntegrationEvents_DoNotExposeForbiddenMemoryPackPropertyTypes()
    {
        var violations = new List<string>();

        foreach (var eventType in ProductionIntegrationEventTypes)
        {
            foreach (var member in GetSerializableMembers(eventType))
            {
                var memberType = member switch
                {
                    PropertyInfo property => property.PropertyType,
                    FieldInfo field => field.FieldType,
                    _ => throw new InvalidOperationException("Unexpected member kind."),
                };

                if (IsForbiddenMemberType(memberType, out var reason))
                {
                    violations.Add($"{eventType.FullName}.{member.Name}: {reason}");
                }
            }
        }

        Assert.HasCount(
            0,
            violations,
            "Integration Event 载荷不得包含 AOT 不可闭合的类型："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void ProductionIntegrationEvents_RoundTripThroughMemoryPackSerializer()
    {
        RoundTrip(
            new TenantProvisionedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-111111111111"),
                "tenant-id",
                "tenant.example"),
            (left, right) =>
                left.TenantId == right.TenantId
                && left.Identifier == right.Identifier
                && left.Domain == right.Domain);
        RoundTrip(
            new TenantChangedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-222222222222"),
                "changed.example"),
            (left, right) =>
                left.TenantId == right.TenantId
                && left.Domain == right.Domain);
        RoundTrip(
            new AnnouncementPublishedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-333333333333"),
                "公告标题"),
            (left, right) =>
                left.AnnouncementId == right.AnnouncementId
                && left.Title == right.Title);
        RoundTrip(
            new InboxMessageReceivedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-444444444444"),
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-555555555555"),
                "站内信标题"),
            (left, right) =>
                left.RecipientUserId == right.RecipientUserId
                && left.MessageId == right.MessageId
                && left.Title == right.Title);
        RoundTrip(
            new InboxReadStateChangedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-666666666666")),
            (left, right) => left.RecipientUserId == right.RecipientUserId);
        RoundTrip(
            new IdentityOrganizationUnitChangedIntegrationEvent(
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-777777777777"),
                Guid.Parse("018f3b2a-7c4e-7b2a-9f3a-888888888888"),
                "研发部",
                true,
                3,
                DateTimeOffset.Parse("2026-08-23T00:00:00Z")),
            (left, right) =>
                left.TenantId == right.TenantId
                && left.UnitId == right.UnitId
                && left.Name == right.Name
                && left.IsActive == right.IsActive
                && left.Version == right.Version
                && left.ChangedAtUtc == right.ChangedAtUtc);
    }

    private static void RoundTrip<T>(
        T original,
        Func<T, T, bool> equivalent)
        where T : notnull
    {
        var bytes = MemoryPackSerializer.Serialize(original);
        Assert.IsGreaterThan(0, bytes.Length, $"{typeof(T).FullName} 序列化结果不得为空。");

        var restored = MemoryPackSerializer.Deserialize<T>(bytes);
        Assert.IsNotNull(restored, $"{typeof(T).FullName} 反序列化不得为 null。");
        Assert.IsTrue(
            equivalent(original, restored),
            $"{typeof(T).FullName} 往返后语义不等价。");
    }

    private static IEnumerable<MemberInfo> GetSerializableMembers(Type eventType) =>
        eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Cast<MemberInfo>()
            .Concat(eventType.GetFields(BindingFlags.Public | BindingFlags.Instance));

    private static bool IsForbiddenMemberType(Type memberType, out string reason)
    {
        if (memberType == typeof(object))
        {
            reason = "禁止 object";
            return true;
        }

        if (memberType.IsInterface)
        {
            reason = $"禁止接口类型 {memberType.Name}";
            return true;
        }

        if (memberType.IsGenericType
            && memberType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && memberType.GenericTypeArguments[0] == typeof(string)
            && memberType.GenericTypeArguments[1] == typeof(object))
        {
            reason = "禁止 Dictionary<string, object>";
            return true;
        }

        if (memberType.IsGenericType)
        {
            var genericDefinition = memberType.GetGenericTypeDefinition();
            if (ForbiddenPropertyTypes.Contains(genericDefinition))
            {
                reason = $"禁止接口集合 {memberType.Name}";
                return true;
            }
        }

        if (memberType.IsArray)
        {
            return IsForbiddenMemberType(memberType.GetElementType()!, out reason);
        }

        if (memberType.IsGenericType)
        {
            foreach (var argument in memberType.GenericTypeArguments)
            {
                if (IsForbiddenMemberType(argument, out reason))
                {
                    reason = $"{memberType.Name} 的类型参数 {reason}";
                    return true;
                }
            }
        }

        reason = string.Empty;
        return false;
    }
}
