using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.FieldProjection;

/// <summary>
/// 维护经安全评审的编译期字段目录，阻止数据库或请求把任意物理列升级为授权字段。
/// </summary>
internal sealed class FieldProjectionCatalog
{
    private readonly IReadOnlyDictionary<string, FieldProjectionResourceDefinition> _resources;

    private FieldProjectionCatalog(
        IReadOnlyCollection<FieldProjectionResourceDefinition> resources)
    {
        var dictionary = new Dictionary<string, FieldProjectionResourceDefinition>(
            StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            ValidateResource(resource);
            if (!dictionary.TryAdd(resource.ResourceKey, resource))
            {
                throw new InvalidOperationException(
                    $"Duplicate field projection resource '{resource.ResourceKey}'.");
            }
        }

        _resources = dictionary;
    }

    public IReadOnlyCollection<FieldProjectionResourceDefinition> Resources =>
        _resources.Values.ToArray();

    public FieldProjectionResourceDefinition GetRequiredResource(string resourceKey)
    {
        if (!_resources.TryGetValue(resourceKey, out var resource))
        {
            throw new InvalidOperationException(
                $"Unknown field projection resource '{resourceKey}'.");
        }

        return resource;
    }

    public bool TryGetResource(
        string resourceKey,
        out FieldProjectionResourceDefinition resource) =>
        _resources.TryGetValue(resourceKey, out resource!);

    public static FieldProjectionCatalog CreateDefault() =>
        new([
            new FieldProjectionResourceDefinition(
                FieldProjectionResourceKeys.HostUsers,
                "Host 用户",
                [
                    Mandatory("id", "标识"),
                    Mandatory("username", "用户名"),
                    Mandatory("display_name", "显示名称"),
                    Mandatory("is_active", "启用状态"),
                    Mandatory("created_at_utc", "创建时间"),
                    Mandatory("updated_at_utc", "更新时间"),
                    Mandatory("version", "版本"),
                    Restricted(
                        "preferred_locale",
                        "区域偏好",
                        FieldProjectionSensitivity.Internal),
                    Restricted(
                        "failed_login_count",
                        "失败登录次数",
                        FieldProjectionSensitivity.Sensitive),
                    Restricted(
                        "lockout_end_utc",
                        "锁定截止时间",
                        FieldProjectionSensitivity.Sensitive),
                ]),
        ]);

    private static FieldProjectionFieldDefinition Mandatory(
        string fieldKey,
        string displayName) =>
        new(
            fieldKey,
            displayName,
            FieldProjectionSensitivity.Public,
            FieldProjectionDefaultVisibility.Mandatory,
            false);

    private static FieldProjectionFieldDefinition Restricted(
        string fieldKey,
        string displayName,
        FieldProjectionSensitivity sensitivity) =>
        new(
            fieldKey,
            displayName,
            sensitivity,
            FieldProjectionDefaultVisibility.Restricted,
            true);

    private static void ValidateResource(FieldProjectionResourceDefinition resource)
    {
        if (string.IsNullOrWhiteSpace(resource.ResourceKey)
            || resource.Fields.Count == 0)
        {
            throw new InvalidOperationException("Field projection resource is invalid.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in resource.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.FieldKey)
                || !keys.Add(field.FieldKey)
                || field.Assignable
                    == (field.DefaultVisibility
                        == FieldProjectionDefaultVisibility.Mandatory))
            {
                throw new InvalidOperationException(
                    $"Field projection resource '{resource.ResourceKey}' has an invalid field.");
            }
        }
    }
}
