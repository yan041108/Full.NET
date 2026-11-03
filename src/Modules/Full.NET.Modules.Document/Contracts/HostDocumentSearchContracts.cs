namespace Full.NET.Modules.Document.Contracts;

/// <summary>搜索仅返回已授权对象的最小展示身份，不包含正文与文件下载地址。</summary>
public sealed record HostDocumentSearchHit(Guid Id, string Title, string? Number);

/// <summary>近期搜索按来源分页；上限截断与没有后续结果分别表达。</summary>
public sealed record HostDocumentSearchPage(IReadOnlyList<HostDocumentSearchHit> Items, int Page, bool HasMore, bool IsTruncated, int WindowDays);
