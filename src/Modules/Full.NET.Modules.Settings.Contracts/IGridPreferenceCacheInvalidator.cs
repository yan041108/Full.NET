namespace Full.NET.Modules.Settings.Contracts;

/// <summary>由 Settings 拥有的 Grid 偏好精确失效入口；调用方不得自行推导缓存键或 Schema 版本。</summary>
public interface IGridPreferenceCacheInvalidator
{
    /// <summary>校验可信 Grid 目录后执行失效；未知 Grid 或空用户标识不触发缓存操作。</summary>
    /// <param name="userId">经调用方授权的目标用户标识。</param>
    /// <param name="gridKey">Settings 发布的稳定 Grid 标识。</param>
    /// <param name="includeSharedCache">是否同步删除共享层并发布通知；不表示所有节点已确认。</param>
    /// <param name="cancellationToken">管理操作取消令牌。</param>
    /// <returns>目录接受并完成操作时为 true；非法目标为 false，基础设施异常向调用方传播。</returns>
    ValueTask<bool> InvalidateAsync(Guid userId, string gridKey, bool includeSharedCache,
        CancellationToken cancellationToken = default);
}
