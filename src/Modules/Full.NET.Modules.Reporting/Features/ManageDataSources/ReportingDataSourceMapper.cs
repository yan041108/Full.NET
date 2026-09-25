using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageDataSources;

/// <summary>报表数据源响应映射。</summary>
internal static class ReportingDataSourceMapper
{
    /// <summary>映射列表项（脱敏）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static ReportingDataSourceListItem MapListItem(ReportingDataSourceRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ProviderKey,
            ReportingDataSourceMasking.MaskServerEndpoint(row.ServerHost, row.Port),
            ReportingDataSourceMasking.MaskIdentifier(row.DatabaseName),
            ReportingDataSourceMasking.MaskIdentifier(row.Username),
            HasPassword(row),
            row.TrustServerCertificate,
            row.IsEnabled,
            row.LastTestedAtUtc,
            row.LastTestStatusKey,
            SafeTestMessage(row),
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    /// <summary>映射详情（不回显密码）。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static ReportingDataSourceResponse MapDetail(ReportingDataSourceRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.ProviderKey,
            row.ServerHost,
            row.Port,
            row.DatabaseName,
            row.Username,
            HasPassword(row),
            row.TrustServerCertificate,
            row.IsEnabled,
            row.LastTestedAtUtc,
            row.LastTestStatusKey,
            SafeTestMessage(row),
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static bool HasPassword(ReportingDataSourceRecord row) =>
        !string.IsNullOrWhiteSpace(row.PasswordProtected);

    private static string? SafeTestMessage(ReportingDataSourceRecord row) =>
        row.LastTestMessage is null
            ? null
            : string.Equals(row.LastTestStatusKey, ReportingDataSourceTestStatusKeys.Succeeded,
                StringComparison.Ordinal)
                ? "Connected successfully. Ensure the account is read-only."
                : "Reporting data source test failed.";
}
