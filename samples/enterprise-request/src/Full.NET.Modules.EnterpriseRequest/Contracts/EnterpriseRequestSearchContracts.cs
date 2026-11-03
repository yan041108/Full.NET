namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>对象搜索只发现当前授权范围内的申请，不投影金额或申请人。</summary>
public sealed record EnterpriseRequestSearchHit(Guid Id, string Title, string Number);

/// <summary>按来源分页与明确截断，不构造跨业务合计。</summary>
public sealed record EnterpriseRequestSearchPage(IReadOnlyList<EnterpriseRequestSearchHit> Items, int Page, bool HasMore, bool IsTruncated, int WindowDays);
