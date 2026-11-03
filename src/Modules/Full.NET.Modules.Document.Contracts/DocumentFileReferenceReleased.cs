using MemoryPack;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>Document 已提交的精确引用释放事实；Files 是实际跨模块消费者。</summary>
public static class DocumentFileReferenceEventTypes
{
    /// <summary>本地引用已移除，Files 可幂等释放对应 Claim。</summary>
    public const string Released = "fullnet.document.file_reference.released";
}

/// <summary>引用标识不可复用；释放按模块和文件双键校验，不能由文件名猜测。</summary>
/// <param name="ReferenceId">不可复用的版本或预览引用标识。</param>
/// <param name="IsPreview">是否按预览引用用途推导 Claim 键。</param>
/// <param name="NotBeforeUtc">有效转换租约结束前不得释放。</param>
/// <param name="FileId">该版本原来引用的精确文件标识。</param>
[MemoryPackable]
public partial record DocumentFileReferenceReleased(Guid ReferenceId, Guid FileId, bool IsPreview = false, DateTimeOffset? NotBeforeUtc = null);
