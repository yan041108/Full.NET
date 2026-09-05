using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述发布版本中固化的一条办理人解析来源。</summary>
/// <param name="ResolverKindKey">指定用户、角色成员、机构负责人、发起人或发起人主部门负责人。</param>
/// <param name="UserIds">指定用户模式下的去重用户标识。</param>
/// <param name="RoleIds">角色成员模式下的去重角色标识。</param>
/// <param name="UnitId">机构负责人模式下的目标机构单元标识。</param>
internal sealed record WorkflowAssigneeSource(
    string ResolverKindKey,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> RoleIds,
    Guid? UnitId);

/// <summary>描述人工审批节点固化的办理人解析策略。</summary>
/// <param name="Sources">按配置顺序排列的闭合解析来源。</param>
internal sealed record WorkflowAssigneePolicy(IReadOnlyList<WorkflowAssigneeSource> Sources)
{
    /// <summary>指定用户解析器键。</summary>
    public const string SpecifiedUsers = "specified_users";

    /// <summary>角色成员解析器键。</summary>
    public const string RoleMembers = "role_members";

    /// <summary>固定机构单元负责人解析器键。</summary>
    public const string OrganizationUnitLeader = "organization_unit_leader";

    /// <summary>流程发起人解析器键。</summary>
    public const string Initiator = "initiator";

    /// <summary>发起人主部门负责人解析器键。</summary>
    public const string InitiatorPrimaryUnitLeader = "initiator_primary_unit_leader";

    private const int MaximumSourceCount = 8;
    private const int MaximumUserCount = 20;
    private const int MaximumRoleCount = 5;

    /// <summary>从人工审批节点配置读取闭合的办理人解析策略；缺失时视为发起人单人语义。</summary>
    /// <param name="config">人工审批节点配置。</param>
    /// <param name="policy">解析成功的策略；未配置时返回默认发起人策略。</param>
    /// <returns>配置结构有效或缺失默认策略时返回 <see langword="true"/>。</returns>
    public static bool TryRead(JsonElement config, out WorkflowAssigneePolicy policy)
    {
        policy = CreateDefault();
        if (config.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!config.TryGetProperty("assigneePolicy", out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("sources", out var sourcesElement) ||
            sourcesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var sources = new List<WorkflowAssigneeSource>();
        foreach (var sourceElement in sourcesElement.EnumerateArray())
        {
            if (!TryReadSource(sourceElement, out var source))
            {
                return false;
            }

            sources.Add(source!);
        }

        if (sources.Count is < 1 or > MaximumSourceCount)
        {
            return false;
        }

        policy = new WorkflowAssigneePolicy(sources);
        return true;
    }

    /// <summary>创建兼容旧定义的默认发起人单人策略。</summary>
    /// <returns>仅包含发起人的默认策略。</returns>
    public static WorkflowAssigneePolicy CreateDefault() =>
        new([new WorkflowAssigneeSource(Initiator, [], [], null)]);

    /// <summary>解析单条办理人来源配置。</summary>
    /// <param name="element">来源 JSON 对象。</param>
    /// <param name="source">解析成功的来源。</param>
    /// <returns>结构、键集合与参数全部闭合时返回 <see langword="true"/>。</returns>
    private static bool TryReadSource(JsonElement element, out WorkflowAssigneeSource? source)
    {
        source = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("resolverKindKey", out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var resolverKindKey = kindElement.GetString();
        return resolverKindKey switch
        {
            SpecifiedUsers => TryReadGuidArray(element, "userIds", 1, MaximumUserCount, out var userIds) &&
                HasExactProperties(element, "resolverKindKey", "userIds") &&
                AssignSource(out source, resolverKindKey!, userIds, [], null),
            RoleMembers => TryReadGuidArray(element, "roleIds", 1, MaximumRoleCount, out var roleIds) &&
                HasExactProperties(element, "resolverKindKey", "roleIds") &&
                AssignSource(out source, resolverKindKey!, [], roleIds, null),
            OrganizationUnitLeader => TryReadGuid(element, "unitId", out var unitId) &&
                HasExactProperties(element, "resolverKindKey", "unitId") &&
                AssignSource(out source, resolverKindKey!, [], [], unitId),
            Initiator => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null),
            InitiatorPrimaryUnitLeader => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null),
            _ => false,
        };
    }

    /// <summary>判断 JSON 对象是否仅包含期望属性。</summary>
    /// <param name="element">待检查对象。</param>
    /// <param name="expected">期望属性名集合。</param>
    /// <returns>属性名集合完全一致时返回 <see langword="true"/>。</returns>
    private static bool HasExactProperties(JsonElement element, params string[] expected) =>
        element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>读取去重后的 Guid 数组属性。</summary>
    /// <param name="element">父对象。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="minCount">最小元素数量。</param>
    /// <param name="maxCount">最大元素数量。</param>
    /// <param name="values">解析结果。</param>
    /// <returns>数组闭合且无重复空标识时返回 <see langword="true"/>。</returns>
    private static bool TryReadGuidArray(
        JsonElement element,
        string propertyName,
        int minCount,
        int maxCount,
        out IReadOnlyList<Guid> values)
    {
        values = [];
        if (!element.TryGetProperty(propertyName, out var arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<Guid>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(item.GetString(), out var value) ||
                value == Guid.Empty ||
                parsed.Contains(value))
            {
                return false;
            }

            parsed.Add(value);
        }

        if (parsed.Count < minCount || parsed.Count > maxCount)
        {
            return false;
        }

        values = parsed;
        return true;
    }

    /// <summary>读取单个 Guid 属性。</summary>
    /// <param name="element">父对象。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>属性存在且为非空 Guid 时返回 <see langword="true"/>。</returns>
    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(propertyName, out var guidElement) &&
            guidElement.ValueKind == JsonValueKind.String &&
            Guid.TryParse(guidElement.GetString(), out value) &&
            value != Guid.Empty;
    }

    /// <summary>构造解析成功的来源记录。</summary>
    private static bool AssignSource(
        out WorkflowAssigneeSource? source,
        string resolverKindKey,
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<Guid> roleIds,
        Guid? unitId)
    {
        source = new WorkflowAssigneeSource(resolverKindKey, userIds, roleIds, unitId);
        return true;
    }
}
