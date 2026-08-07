namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 读取 Identity 本地维护的 Organization 机构单元投影，供数据范围等用例失败关闭校验。
/// </summary>
public interface IOrganizationUnitProjectionDirectory
{
    /// <summary>
    /// 查找指定租户下的活动机构单元投影；缺失、停用或版本过旧时返回 <see langword="null"/>。
    /// </summary>
    Task<OrganizationUnitProjectionEntry?> FindActiveUnitAsync(
        Guid tenantId,
        Guid unitId,
        CancellationToken cancellationToken = default);
}

/// <summary>Identity 消费的最小机构单元投影项。</summary>
public sealed record OrganizationUnitProjectionEntry(
    Guid UnitId,
    string Name);
