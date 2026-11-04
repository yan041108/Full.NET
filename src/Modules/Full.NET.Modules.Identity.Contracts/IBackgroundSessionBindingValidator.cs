namespace Full.NET.Modules.Identity.Contracts;

/// <summary>无 HTTP 上下文时复核冻结会话绑定；撤销、轮换、租户切换或成员失效均拒绝原租户准入。</summary>
public interface IBackgroundSessionBindingValidator
{
    /// <summary>复核会话绑定快照在当前时刻是否仍然有效。</summary>
    /// <param name="binding">冻结的会话绑定快照。</param>
    /// <param name="cancellationToken">取消校验的令牌。</param>
    /// <returns>会话及租户准入仍然有效时为 true；会话、租户或成员准入失效后为 false。</returns>
    Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default);
}
