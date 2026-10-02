namespace Full.NET.Modules.Identity.Contracts;

/// <summary>无 HTTP 上下文时复核冻结会话绑定；撤销、轮换或租户切换立即失效。</summary>
public interface IBackgroundSessionBindingValidator
{
    /// <summary>复核会话绑定快照在当前时刻是否仍然有效。</summary>
    /// <param name="binding">冻结的会话绑定快照。</param>
    /// <param name="cancellationToken">取消校验的令牌。</param>
    /// <returns>会话绑定仍然有效时为 true；会话已撤销、轮换或租户切换后为 false。</returns>
    Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default);
}
