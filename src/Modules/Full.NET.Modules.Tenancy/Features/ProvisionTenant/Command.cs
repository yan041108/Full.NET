using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

/// <summary>
/// 请求在 Host 作用域下开通一个新租户。
/// </summary>
/// <param name="Identifier">租户标识符；服务端会做裁剪、格式校验和唯一性检查。</param>
/// <param name="Name">租户展示名称。</param>
/// <param name="Domain">租户默认域名标识；服务端会按统一规则归一化并校验唯一性。</param>
/// <param name="TenantPackageId">可选的租户套餐标识；为空时表示按无套餐租户开通。</param>
internal sealed record ProvisionTenantCommand(
    string Identifier,
    string Name,
    string Domain,
    Guid? TenantPackageId = null) : ITransactionalCommand<TenantSummary>;
