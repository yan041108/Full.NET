namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

/// <summary>Host 分享分页查询的可选筛选与排序参数（来自 GET query）。</summary>
internal sealed record HostDocumentShareListQuery(
    bool? IsEnabled = null,
    string? ShareCode = null,
    string? DocumentId = null,
    bool ExpiredOnly = false,
    bool ActiveOnly = false,
    int? MinAccessCount = null,
    int? MaxAccessCount = null,
    string? SortBy = null,
    string? SortDir = null);
