namespace Full.NET.Caching.Abstractions;

/// <summary>执行有界的缓存失效批次；复用单目标失效语义，不建立新的广播链路。</summary>
public interface ICacheBatchInvalidator
{
    /// <summary>整批预检并去重后执行精确失效；单个目标失败仍尝试其余目标。</summary>
    /// <param name="targets">拥有者产生的完整键或登记标签；不得从不受控管理输入直接构造。</param>
    /// <param name="cancellationToken">调用方取消令牌；提交后调用者须传入独立清理令牌。</param>
    /// <returns>所有目标成功时完成；失败时保留最先失败输入的原异常与堆栈。</returns>
    /// <remarks>条目、目标类型和传播范围共同确定去重身份；不同环境/租户的完整键不会合并。目标最多 1024 UTF-8 字节。总期限包括并发等待，不承诺跨节点确认；批次容量到顶立即拒绝。</remarks>
    ValueTask RemoveAsync(IEnumerable<CacheInvalidationTarget> targets, CancellationToken cancellationToken = default);
}

/// <summary>一项拥有者登记的精确失效目标。</summary>
/// <param name="EntryName">已登记的稳定条目名。</param>
/// <param name="Target">包含环境/租户边界的完整键，或拥有者登记的标签。</param>
/// <param name="Kind">键或标签失效。</param>
/// <param name="Scope">仅当前节点或全层同步范围。</param>
public sealed record CacheInvalidationTarget(string EntryName, string Target,
    CacheInvalidationTargetKind Kind, CacheInvalidationScope Scope);

/// <summary>精确缓存失效目标类型。</summary>
public enum CacheInvalidationTargetKind
{
    /// <summary>删除完整缓存键。</summary>
    Key,
    /// <summary>失效登记标签下的条目。</summary>
    Tag,
}
