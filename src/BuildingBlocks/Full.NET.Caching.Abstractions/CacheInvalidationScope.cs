namespace Full.NET.Caching.Abstractions;

/// <summary>
/// 缓存失效传播范围。调用方只声明一致性意图，不接触具体缓存 Provider 的执行选项。
/// </summary>
public enum CacheInvalidationScope
{
    /// <summary>仅清理当前节点 L1；不得访问共享 L2 或发布 Backplane 通知。</summary>
    CurrentNodeOnly = 0,

    /// <summary>同步清理当前节点与共享 L2，并等待 Backplane 发布；Provider 失败必须向调用方传播。</summary>
    AllLayersSynchronous = 1,
}
