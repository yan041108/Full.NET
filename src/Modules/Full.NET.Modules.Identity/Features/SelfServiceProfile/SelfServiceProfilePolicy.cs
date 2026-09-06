using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>
/// 收敛自助资料可写字段边界：敏感揭示字段与 HR/内部运维字段只读，租户/角色/启停不在此端点暴露。
/// </summary>
internal static class SelfServiceProfilePolicy
{
    private static readonly string[] ReadOnlyProfileFieldKeys =
    [
        "phone_number",
        "id_card_number",
        "id_card_type",
        "employee_number",
        "sort_order",
        "join_date_utc",
    ];

    /// <summary>判断当前会话是否处于 Host 参与者范围。</summary>
    public static bool IsHostActorScope(string actorScope) =>
        string.Equals(actorScope, "host", StringComparison.Ordinal);

    /// <summary>在字段投影可读集合上剔除自助只读字段，得到可写档案键。</summary>
    public static IReadOnlyList<string> GetWritableProfileFieldKeys(
        IReadOnlyCollection<string>? effectiveFieldKeys) =>
        HostUserProfileMapper.GetWritableFieldKeys(effectiveFieldKeys)
            .Where(fieldKey =>
                !ReadOnlyProfileFieldKeys.Contains(fieldKey, StringComparer.Ordinal))
            .ToArray();

    /// <summary>拒绝客户端显式提交只读档案字段键。</summary>
    public static Error? ValidateNoReadOnlyFieldsInPatch(
        IReadOnlyCollection<string>? submittedFieldKeys)
    {
        if (submittedFieldKeys is not { Count: > 0 })
        {
            return null;
        }

        var forbidden = submittedFieldKeys
            .Where(fieldKey =>
                ReadOnlyProfileFieldKeys.Contains(fieldKey, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (forbidden.Length == 0)
        {
            return null;
        }

        return new Error(
            IdentityErrorCodes.SelfServiceProfileReadOnlyFieldRejected,
            "Self-service profile update cannot modify read-only fields.",
            ErrorType.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["fieldKeys"] = forbidden,
            });
    }
}
