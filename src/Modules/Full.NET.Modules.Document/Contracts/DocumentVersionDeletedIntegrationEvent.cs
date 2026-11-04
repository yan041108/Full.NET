using global::MemoryPack;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>版本物理删除与本地事务原子提交的事实；不包含可覆盖租户上下文的字段。</summary>
/// <param name="DocumentItemId">所属 Host 文档标识。</param>
/// <param name="VersionId">不可复用的文档版本标识，也是文件引用幂等键的来源。</param>
/// <param name="FileId">被删除版本原来引用的文件标识。</param>
[MemoryPackable]
internal sealed partial record DocumentVersionDeletedIntegrationEvent(Guid DocumentItemId, Guid VersionId, Guid FileId);
