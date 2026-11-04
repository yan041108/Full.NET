namespace Full.NET.Modules.Identity.Contracts;

/// <summary>使用独立数据作用域权威复核当前交互权限，禁止加入调用方业务事务。</summary>
public interface IIndependentCurrentSessionAuthorization
{
    /// <summary>宿主是否为提交前独立读取留下专用准入与物理连接池容量；不证明数据库健康。</summary>
    bool HasIndependentReadCapacity { get; }
    /// <summary>身份和租户仍来自当前可信上下文，调用方不能传入用户、会话或租户覆盖。</summary>
    /// <param name="permissionCode">精确权限码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>独立权威读取后的最小主体；上下文或权限失效时为空。</returns>
    Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>在同一独立作用域中读取一次权威会话和权限快照，要求全部精确权限成立。</summary>
    /// <param name="permissionCodes">非空权限集合；进入异步读取前冻结，不接受空权限码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部权限均成立且原可信上下文不变时的主体，否则为空。</returns>
    Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default);
}
