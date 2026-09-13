namespace Full.NET.Modules.Identity.Contracts;

/// <summary>无 HTTP 上下文时复核冻结会话绑定；撤销、轮换或租户切换立即失效。</summary>
public interface IBackgroundSessionBindingValidator
{
    Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default);
}
