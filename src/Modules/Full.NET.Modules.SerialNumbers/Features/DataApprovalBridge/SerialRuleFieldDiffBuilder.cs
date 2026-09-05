using System.Text.Json;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Serialization;

namespace Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

/// <summary>构建流水号规则更新审批的字段级差异，供 API 与 Vue 展示。</summary>
internal static class SerialRuleFieldDiffBuilder
{
    /// <summary>比较变更前快照与提议更新，返回稳定字段差异列表。</summary>
    /// <param name="beforeSnapshotJson">变更前快照 JSON。</param>
    /// <param name="proposed">提议更新请求。</param>
    public static IReadOnlyList<SerialRuleFieldChange> Build(
        string beforeSnapshotJson,
        UpdateSerialNumberRuleRequest proposed)
    {
        var before = JsonSerializer.Deserialize(
            beforeSnapshotJson,
            SerialNumbersJsonSerializerContext.Default.UpdateSerialNumberRuleRequest);
        if (before is null)
        {
            return [];
        }

        return
        [
            Change("displayName", before.DisplayName, proposed.DisplayName),
            Change("description", before.Description, proposed.Description),
            Change("scope", FormatScope(before.Scope), FormatScope(proposed.Scope)),
            Change("resetInterval", FormatResetInterval(before.ResetInterval), FormatResetInterval(proposed.ResetInterval)),
            Change("pattern", before.Pattern, proposed.Pattern),
            Change("minimumValue", before.MinimumValue.ToString(), proposed.MinimumValue.ToString()),
            Change("maximumValue", before.MaximumValue.ToString(), proposed.MaximumValue.ToString()),
            Change("displayOrder", before.DisplayOrder.ToString(), proposed.DisplayOrder.ToString()),
            Change("isEnabled", before.IsEnabled.ToString(), proposed.IsEnabled.ToString()),
        ];
    }

    /// <summary>判断提议更新是否与当前快照存在任何业务字段差异。</summary>
    /// <param name="changes">字段差异列表。</param>
    public static bool HasChanges(IReadOnlyList<SerialRuleFieldChange> changes) =>
        changes.Any(change => change.Changed);

    private static SerialRuleFieldChange Change(
        string fieldKey,
        string? before,
        string? after)
    {
        var normalizedBefore = Normalize(before);
        var normalizedAfter = Normalize(after);
        return new SerialRuleFieldChange(
            fieldKey,
            normalizedBefore,
            normalizedAfter,
            !string.Equals(normalizedBefore, normalizedAfter, StringComparison.Ordinal));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string FormatScope(SerialNumberRuleScope scope) =>
        scope.ToString();

    private static string FormatResetInterval(SerialNumberResetInterval resetInterval) =>
        resetInterval.ToString();
}
