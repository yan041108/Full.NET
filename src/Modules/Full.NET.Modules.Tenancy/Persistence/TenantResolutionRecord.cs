using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>
/// 不含套餐 JOIN 的租户投影记录，供 Dapper 解析 7 列 Global/租户上下文查询。
/// </summary>
internal sealed record TenantResolutionRecord(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version,
    string DefaultLocale)
{
    internal TenantSummary ToSummary() => new(
        Id,
        Identifier,
        Name,
        Domain,
        IsActive,
        Version,
        DefaultLocale);
}
