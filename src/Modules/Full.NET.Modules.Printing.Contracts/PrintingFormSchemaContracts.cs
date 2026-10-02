namespace Full.NET.Modules.Printing.Contracts;

/// <summary>固定表单字段定义。</summary>
/// <param name="FieldKey">字段键；用于布局占位符与数据绑定。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record PrintingFormFieldDefinition(
    string FieldKey,
    string DisplayName);

/// <summary>固定表单 Schema 定义。</summary>
/// <param name="FormSchemaKey">稳定 Schema 键。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">说明。</param>
/// <param name="Fields">允许绑定的字段集合。</param>
public sealed record PrintingFormSchemaDefinition(
    string FormSchemaKey,
    string DisplayName,
    string Description,
    IReadOnlyList<PrintingFormFieldDefinition> Fields);

/// <summary>租户档案卡片绑定数据；由 Tenancy 模块通过契约端口提供。</summary>
/// <param name="TenantName">租户显示名称。</param>
/// <param name="TenantCode">租户稳定编码。</param>
/// <param name="TenantDomain">租户主域名。</param>
public sealed record PrintingTenantProfileBinding(
    string TenantName,
    string TenantCode,
    string TenantDomain);

/// <summary>跨模块租户档案绑定源；由 Tenancy 实现，Printing 只消费契约。</summary>
public interface IPrintingTenantProfileBindingSource
{
    /// <summary>解析指定租户的档案绑定字段。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租户档案绑定数据；租户不存在或非活动时为 <see langword="null"/>。</returns>
    Task<PrintingTenantProfileBinding?> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
