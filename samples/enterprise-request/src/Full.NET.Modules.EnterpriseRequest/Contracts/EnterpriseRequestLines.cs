using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>明细编辑只接收业务值；身份、行号和金额由服务端确定。</summary>
public sealed record EnterpriseRequestLineInput(string ItemDescription,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal UnitPrice);

/// <summary>整组替换以主表版本保护明细、合计与审批状态的一致性。</summary>
public sealed record ReplaceEnterpriseRequestLinesRequest(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version,
    IReadOnlyList<EnterpriseRequestLineInput>? Items);

/// <summary>服务端计算后的明细行，十进制保持精确字符串线格式。</summary>
public sealed record EnterpriseRequestLineResponse(Guid Id, int LineNumber, string ItemDescription,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal UnitPrice,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal LineAmount);

/// <summary>主表与明细的同版本快照；空明细允许保留历史手填总额。</summary>
public sealed record EnterpriseRequestLinesResponse(Guid RequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RequestVersion,
    string RequestStatus,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal TotalAmount,
    IReadOnlyList<EnterpriseRequestLineResponse> Items);
