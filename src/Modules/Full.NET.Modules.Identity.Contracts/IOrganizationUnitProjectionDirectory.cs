namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 读取 Identity 本地维护的 Organization 机构单元投影，供数据范围等用例失败关闭校验。
/// </summary>
public interface IOrganizationUnitProjectionDirectory
{
    /// <summary>
    /// 查找指定租户下的活动机构单元投影；缺失、停用或版本过旧时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="unitId">待查询的机构单元标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>活动且未过期的投影项；缺失、停用或对账版本过旧时返回 <see langword="null"/>。</returns>
    Task<OrganizationUnitProjectionEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default);
}

/// <summary>Identity 消费的最小机构单元投影项。</summary>
/// <param name="UnitId">机构单元稳定标识。</param>
/// <param name="Name">机构单元名称快照。</param>
public sealed record OrganizationUnitProjectionEntry(
    Guid UnitId,
    string Name);
