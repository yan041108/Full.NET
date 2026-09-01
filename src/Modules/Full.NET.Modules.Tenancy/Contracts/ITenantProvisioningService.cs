using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 开通新租户的显式请求；标识符与域名创建后不可变。
/// </summary>
/// <param name="Identifier">稳定租户编码；在 Host 作用域内唯一且不可更改。</param>
/// <param name="Name">租户显示名称；可在后续通过管理 API 变更。</param>
/// <param name="Domain">租户主域名；在 Host 作用域内唯一且不可更改。</param>
/// <param name="TenantPackageId">可选的初始套餐标识；<see langword="null"/> 表示不绑定套餐。</param>
public sealed record ProvisionTenantRequest(
    string Identifier,
    string Name,
    string Domain,
    Guid? TenantPackageId = null);

/// <summary>
/// 提供显式、幂等的租户开通能力。
/// </summary>
/// <remarks>
/// 实现方须确保 Identifier 与 Domain 的唯一约束；重复提交相同 Identifier 不得产生重复租户，
/// 并保持并发场景下最后写入不覆盖已生效记录。
/// </remarks>
public interface ITenantProvisioningService
{
    /// <summary>
    /// 按请求开通新租户并返回摘要；重复提交相同 Identifier 时返回既有租户，视为幂等成功。
    /// </summary>
    /// <param name="request">显式提供的租户开通资料；调用方必须确保 Identifier 与 Domain 来自受控命名空间。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>返回开通完成或已存在的租户摘要；唯一约束冲突时返回稳定失败码。</returns>
    Task<Result<TenantSummary>> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);
}
