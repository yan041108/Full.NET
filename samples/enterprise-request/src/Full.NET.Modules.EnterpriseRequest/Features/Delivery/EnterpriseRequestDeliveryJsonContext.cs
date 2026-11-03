using System.Text.Json.Serialization;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>交付 HTTP 与摘要的静态序列化闭包，不对业务对象启用反射回退。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EnterpriseDeliverySnapshot))]
[JsonSerializable(typeof(EnterpriseRequestSearchPage))]
[JsonSerializable(typeof(EnterpriseRequestAttachmentResponse))]
[JsonSerializable(typeof(IReadOnlyList<EnterpriseRequestAttachmentResponse>))]
[JsonSerializable(typeof(DeleteEnterpriseRequestAttachmentRequest))]
[JsonSerializable(typeof(PagedResult<EnterpriseRequestAttachmentResponse>))]
[JsonSerializable(typeof(PagedResult<EnterpriseRequestReportResponse>))]
[JsonSerializable(typeof(CancelEnterpriseRequestReportRequest))]
[JsonSerializable(typeof(CreateEnterpriseRequestReportRequest))]
[JsonSerializable(typeof(EnterpriseRequestReportResponse))]
[JsonSerializable(typeof(IReadOnlyList<EnterpriseRequestReportResponse>))]
[JsonSerializable(typeof(EnterpriseRequestPrintResponse))]
internal partial class EnterpriseRequestDeliveryJsonContext : JsonSerializerContext;
