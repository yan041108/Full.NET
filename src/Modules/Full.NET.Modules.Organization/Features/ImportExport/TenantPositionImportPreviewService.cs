using System.Text.RegularExpressions;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;

namespace Full.NET.Modules.Organization.Features.ImportExport;

/// <summary>租户职位静态导入预校验；只验证结构与权限，不产生数据库写入。</summary>
internal sealed class TenantPositionImportPreviewService
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>对解析后的职位行执行无副作用预校验。</summary>
    public StaticImportPreviewResult Preview(
        IReadOnlyList<ImportOrganizationPositionRow> rows,
        StaticImportPreviewContext context)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var canAssignUnit = HasCapability(
            context,
            OrganizationPositionManagementPermissions.AssignUnit);
        var canAssignPositionLevel = HasCapability(
            context,
            OrganizationPositionManagementPermissions.AssignPositionLevel);
        var results = new List<StaticImportRowPreviewResult>(rows.Count);
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var line = 0;
        var validCount = 0;

        foreach (var row in rows)
        {
            line++;
            if (row is null)
            {
                results.Add(InvalidRow(line, ValidationErrorCodes.Failed, "Import row is required."));
                continue;
            }

            var normalizedCode = row.Code?.Trim() ?? string.Empty;
            if (!CodePattern.IsMatch(normalizedCode))
            {
                results.Add(InvalidRow(
                    line,
                    ValidationErrorCodes.Failed,
                    "Position code is invalid."));
                continue;
            }

            if (!seenCodes.Add(normalizedCode))
            {
                results.Add(InvalidRow(
                    line,
                    OrganizationErrorCodes.PositionImportDuplicateCode,
                    "Duplicate position code in import workbook."));
                continue;
            }

            var normalizedName = row.Name?.Trim() ?? string.Empty;
            if (normalizedName.Length is < 1 or > 128)
            {
                results.Add(InvalidRow(
                    line,
                    ValidationErrorCodes.Failed,
                    "Position name is invalid."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.UnitCode) && !canAssignUnit)
            {
                results.Add(InvalidRow(
                    line,
                    CommonErrorCodes.PermissionDenied,
                    "Importing organization unit assignment is not allowed."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.PositionLevelCode) && !canAssignPositionLevel)
            {
                results.Add(InvalidRow(
                    line,
                    CommonErrorCodes.PermissionDenied,
                    "Importing position level assignment is not allowed."));
                continue;
            }

            validCount++;
            results.Add(new StaticImportRowPreviewResult(line, true, null, null));
        }

        return new StaticImportPreviewResult(
            rows.Count,
            validCount,
            rows.Count - validCount,
            results);
    }

    private static bool HasCapability(StaticImportPreviewContext context, string permissionCode) =>
        context.CapabilityFlags.TryGetValue(permissionCode, out var allowed) && allowed;

    private static StaticImportRowPreviewResult InvalidRow(
        int lineNumber,
        string errorCode,
        string message) =>
        new(lineNumber, false, errorCode, message);
}
