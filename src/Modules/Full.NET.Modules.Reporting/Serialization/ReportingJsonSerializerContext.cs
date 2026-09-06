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
[JsonSerializable(typeof(ReportingGroupResponse))]
[JsonSerializable(typeof(CreateReportingGroupRequest))]
[JsonSerializable(typeof(UpdateReportingGroupRequest))]
[JsonSerializable(typeof(ReportingDefinitionResponse))]
[JsonSerializable(typeof(ReportingDefinitionVersionResponse))]
[JsonSerializable(typeof(CreateReportingDefinitionRequest))]
[JsonSerializable(typeof(UpdateReportingDefinitionRequest))]
[JsonSerializable(typeof(ReportingExecutionPageResponse))]
[JsonSerializable(typeof(ReportingExecutionColumnDefinition))]
[JsonSerializable(typeof(ReportingExecutionRow))]
[JsonSerializable(typeof(ReportingExecutionParameterValue))]
[JsonSerializable(typeof(ExecuteReportingDefinitionRequest))]
[JsonSerializable(typeof(PublishReportingDefinitionRequest))]
[JsonSerializable(typeof(ReportingQueryPortDefinition))]
[JsonSerializable(typeof(ReportingQueryPortParameterDefinition))]
[JsonSerializable(typeof(ReportingParameterSchemaEntry))]
internal partial class ReportingJsonSerializerContext : JsonSerializerContext;
