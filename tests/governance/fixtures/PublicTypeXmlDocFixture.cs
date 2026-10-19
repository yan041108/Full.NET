namespace Full.NET.Governance.Fixtures;

/// <summary>属性与主类型之间的多行 XML 文档应被审计脚本识别。</summary>
/// <param name="Id">示例标识。</param>
[System.Obsolete("fixture only")]
public sealed record DocumentedRecordWithAttributes(Guid Id);

/// <summary>Azure OpenAI 目录探测；部署可能不在列表中，沿用人工核对语义。</summary>
public sealed record ChineseSummaryWithProductNamePrefix(Guid Id);
