namespace Full.NET.Modules.Organization.Persistence;

/// <summary>已提交导入行的稳定结果与载荷摘要；未完成占位不允许独立提交。</summary>
internal sealed record PositionImportReceiptRecord(Guid? PositionId, string PayloadHash);
