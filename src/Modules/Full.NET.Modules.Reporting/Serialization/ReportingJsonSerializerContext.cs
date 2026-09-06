using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Serialization;

/// <summary>Reporting 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PagedResult<ReportingDataSourceListItem>))]
[JsonSerializable(typeof(ReportingDataSourceListItem))]
[JsonSerializable(typeof(ReportingDataSourceResponse))]
[JsonSerializable(typeof(CreateReportingDataSourceRequest))]
[JsonSerializable(typeof(UpdateReportingDataSourceRequest))]
[JsonSerializable(typeof(TestReportingDataSourceResult))]
internal partial class ReportingJsonSerializerContext : JsonSerializerContext;
