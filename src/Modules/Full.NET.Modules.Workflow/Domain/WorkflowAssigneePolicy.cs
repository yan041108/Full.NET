using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>描述发布版本中固化的一条办理人解析来源。</summary>
/// <param name="ResolverKindKey">指定用户、角色成员、机构负责人、发起人或发起人上级部门负责人。</param>
/// <param name="UserIds">指定用户模式下的去重用户标识。</param>
/// <param name="RoleIds">角色成员模式下的去重角色标识。</param>
/// <param name="UnitId">机构负责人模式下的目标机构单元标识。</param>
/// <param name="AncestorLevel">指定上级机构的层级，或连续主管来源最多解析的级数。</param>
/// <param name="PositionIds">岗位成员来源的明确岗位集合。</param>
/// <param name="FieldKey">发布表单中承载稳定人员或部门标识的引用字段。</param>
/// <param name="PreviousNodeKey">必经先行活动的节点键，用于读取真实处理人。</param>
/// <param name="StarterSelection">发起人自选的固定模式、数量和范围。</param>
internal sealed record WorkflowAssigneeSource(
    string ResolverKindKey,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> RoleIds,
    Guid? UnitId,
    int? AncestorLevel,
    IReadOnlyList<Guid>? PositionIds = null,
    string? FieldKey = null,
    string? PreviousNodeKey = null,
    WorkflowStarterSelectionPolicy? StarterSelection = null);

/// <summary>描述人工审批节点固化的办理人解析策略。</summary>
/// <param name="Sources">按配置顺序排列的闭合解析来源。</param>
/// <param name="Handling">显式无人/本人处置；未声明时保持失败关闭与本人继续办理。</param>
internal sealed record WorkflowAssigneePolicy(IReadOnlyList<WorkflowAssigneeSource> Sources, WorkflowAssigneeHandlingPolicy? Handling = null)
{
    /// <summary>发布时也核对替补引用，运行时只有本次实际采用的来源进入固定成员快照。</summary>
    public IEnumerable<WorkflowAssigneeSource> EnumerateReferencedSources() => Sources.Concat(Handling is null ? [] :
        Handling.WhenEmpty.FallbackPolicies.Concat(Handling.WhenInitiator.FallbackPolicies).SelectMany(policy => policy.Sources));
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
    /// <summary>Organization 明确维护的发起人直属上级，不等同于部门负责人。</summary>
    public const string InitiatorDirectManager = "initiator_direct_manager";
    /// <summary>开始节点中明确维护的该发布版本流程管理员。</summary>
    public const string FlowAdministrators = "flow_administrators";

    /// <summary>发起人上级部门负责人解析器键。</summary>
    public const string InitiatorAncestorUnitLeader = "initiator_ancestor_unit_leader";

    /// <summary>活动岗位成员来源。</summary>
    public const string PositionMembers = "position_members";
    /// <summary>活动机构成员来源。</summary>
    public const string OrganizationUnitMembers = "organization_unit_members";
    /// <summary>从发起人主部门开始的有界连续主管链。</summary>
    public const string InitiatorLeaderChain = "initiator_leader_chain";
    /// <summary>已发布表单的人员引用字段。</summary>
    public const string FormUsers = "form_users";
    /// <summary>已发布表单的部门引用字段。</summary>
    public const string FormUnits = "form_units";
    /// <summary>支配当前节点的已完成活动实际处理人。</summary>
    public const string PreviousActors = "previous_node_actors";
    /// <summary>发起人在发布范围内提供的不可变启动选择。</summary>
    public const string StarterSelectedUsers = "starter_selected_users";

    private const int MaximumSourceCount = 8;
    private const int MaximumUserCount = 20;
    private const int MaximumRoleCount = 5;

    /// <summary>读取闭合选人配置；仅无显式多人列表时缺省为发起人。</summary>
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
            // 多人列表本身就是明确成员，不能把单人默认值额外计入会签分母。
            if (config.TryGetProperty("approvalPolicy", out _)) policy = new([]);
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object || !HasPolicyProperties(element) ||
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

        return TryCreatePolicy(element, sources, out policy);
    }

    /// <summary>从独立的办理人策略对象读取闭合配置，供设计器预览复用。</summary>
    /// <param name="assigneePolicyElement">办理人策略 JSON 对象。</param>
    /// <param name="policy">解析成功的策略。</param>
    /// <returns>结构有效时返回 <see langword="true"/>。</returns>
    public static bool TryReadPolicy(JsonElement assigneePolicyElement, out WorkflowAssigneePolicy policy)
    {
        policy = CreateDefault();
        if (assigneePolicyElement.ValueKind != JsonValueKind.Object || !HasPolicyProperties(assigneePolicyElement) ||
            !assigneePolicyElement.TryGetProperty("sources", out var sourcesElement) ||
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

        return TryCreatePolicy(assigneePolicyElement, sources, out policy);
    }

    private static bool HasPolicyProperties(JsonElement element) => element.TryGetProperty("handling", out _)
        ? HasExactProperties(element, "sources", "handling") : HasExactProperties(element, "sources");

    private static bool TryCreatePolicy(JsonElement element, IReadOnlyList<WorkflowAssigneeSource> sources, out WorkflowAssigneePolicy policy)
    {
        policy = CreateDefault(); WorkflowAssigneeHandlingPolicy? handling = null;
        if (element.TryGetProperty("handling", out var value) && !WorkflowAssigneeHandlingPolicy.TryRead(value, out handling)) return false;
        policy = new(sources, handling); return true;
    }

    /// <summary>创建兼容旧定义的默认发起人单人策略。</summary>
    /// <returns>仅包含发起人的默认策略。</returns>
    public static WorkflowAssigneePolicy CreateDefault() =>
        new([new WorkflowAssigneeSource(Initiator, [], [], null, null)]);

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
            StarterSelectedUsers => HasExactProperties(element, "resolverKindKey", "selectionPolicy") && element.TryGetProperty("selectionPolicy", out var selection) &&
                WorkflowStarterSelectionPolicy.TryRead(selection, out var selected) && AssignSelection(out source, selected!),
            FormUsers or FormUnits => TryReadKey(element, "fieldKey", out var fieldKey) && HasExactProperties(element, "resolverKindKey", "fieldKey") &&
                AssignDynamic(out source, resolverKindKey!, fieldKey, null),
            PreviousActors => TryReadKey(element, "nodeKey", out var nodeKey) && HasExactProperties(element, "resolverKindKey", "nodeKey") &&
                AssignDynamic(out source, resolverKindKey!, null, nodeKey),
            SpecifiedUsers => TryReadGuidArray(element, "userIds", 1, MaximumUserCount, out var userIds) &&
                HasExactProperties(element, "resolverKindKey", "userIds") &&
                AssignSource(out source, resolverKindKey!, userIds, [], null, null),
            RoleMembers => TryReadGuidArray(element, "roleIds", 1, MaximumRoleCount, out var roleIds) &&
                HasExactProperties(element, "resolverKindKey", "roleIds") &&
                AssignSource(out source, resolverKindKey!, [], roleIds, null, null),
            OrganizationUnitLeader => TryReadGuid(element, "unitId", out var unitId) &&
                HasExactProperties(element, "resolverKindKey", "unitId") &&
                AssignSource(out source, resolverKindKey!, [], [], unitId, null),
            Initiator => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null, null),
            InitiatorPrimaryUnitLeader => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null, null),
            InitiatorDirectManager => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null, null),
            FlowAdministrators => HasExactProperties(element, "resolverKindKey") &&
                AssignSource(out source, resolverKindKey!, [], [], null, null),
            InitiatorAncestorUnitLeader => TryReadAncestorLevel(element, out var ancestorLevel) &&
                HasExactProperties(element, "resolverKindKey", "ancestorLevel") &&
                AssignSource(out source, resolverKindKey!, [], [], null, ancestorLevel),
            PositionMembers => TryReadGuidArray(element, "positionIds", 1, 5, out var positions) &&
                HasExactProperties(element, "resolverKindKey", "positionIds") &&
                AssignSource(out source, resolverKindKey!, [], [], null, null, positions),
            OrganizationUnitMembers => TryReadGuid(element, "unitId", out var membersUnitId) &&
                HasExactProperties(element, "resolverKindKey", "unitId") &&
                AssignSource(out source, resolverKindKey!, [], [], membersUnitId, null),
            InitiatorLeaderChain => element.TryGetProperty("maximumLevels", out var levelsElement) &&
                levelsElement.ValueKind == JsonValueKind.Number && levelsElement.TryGetInt32(out var levels) && levels is >= 1 and <= 20 &&
                HasExactProperties(element, "resolverKindKey", "maximumLevels") &&
                AssignSource(out source, resolverKindKey!, [], [], null, levels),
            _ => false,
        };
    }

    private static bool AssignSelection(out WorkflowAssigneeSource? source, WorkflowStarterSelectionPolicy policy)
    {
        source = new(StarterSelectedUsers, [], [], null, null, StarterSelection: policy); return true;
    }

    private static bool TryReadKey(JsonElement element, string property, out string? key)
    {
        key = null;
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return false;
        key = value.GetString();
        return WorkflowNodeKeyValidator.IsValid(key);
    }

    private static bool AssignDynamic(out WorkflowAssigneeSource? source, string kind, string? field, string? node)
    {
        source = new(kind, [], [], null, null, FieldKey: field, PreviousNodeKey: node);
        return true;
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

    /// <summary>读取上级部门层级参数。</summary>
    /// <param name="element">父对象。</param>
    /// <param name="ancestorLevel">解析结果。</param>
    /// <returns>层级在受控闭区间内时返回 <see langword="true"/>。</returns>
    private static bool TryReadAncestorLevel(JsonElement element, out int ancestorLevel)
    {
        ancestorLevel = 0;
        if (!element.TryGetProperty("ancestorLevel", out var levelElement))
        {
            return false;
        }

        if (levelElement.ValueKind == JsonValueKind.Number && levelElement.TryGetInt32(out ancestorLevel))
        {
            return ancestorLevel is >= 1 and <= 20;
        }

        return levelElement.ValueKind == JsonValueKind.String &&
            int.TryParse(levelElement.GetString(), out ancestorLevel) &&
            ancestorLevel is >= 1 and <= 20;
    }

    /// <summary>构造解析成功的来源记录。</summary>
    private static bool AssignSource(
        out WorkflowAssigneeSource? source,
        string resolverKindKey,
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<Guid> roleIds,
        Guid? unitId,
        int? ancestorLevel,
        IReadOnlyList<Guid>? positionIds = null)
    {
        source = new WorkflowAssigneeSource(resolverKindKey, userIds, roleIds, unitId, ancestorLevel, positionIds);
        return true;
    }
}
