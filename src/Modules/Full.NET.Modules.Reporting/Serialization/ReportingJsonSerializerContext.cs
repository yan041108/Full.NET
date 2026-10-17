using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Serialization;

/// <summary>Reporting 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PagedResult<ReportingDataSourceListItem>))]
[JsonSerializable(typeof(PagedResult<Guid>))]
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
[JsonSerializable(typeof(CreateReportingExportTaskRequest))]
[JsonSerializable(typeof(ReportingExportTaskResponse))]
[JsonSerializable(typeof(ReportingExportTaskDetailResponse))]
[JsonSerializable(typeof(PagedResult<ReportingExportTaskResponse>))]
[JsonSerializable(typeof(PublishReportingDefinitionRequest))]
[JsonSerializable(typeof(ReportingQueryPortDefinition))]
[JsonSerializable(typeof(ReportingQueryPortParameterDefinition))]
[JsonSerializable(typeof(ReportingParameterSchemaEntry))]
[JsonSerializable(typeof(IReadOnlyList<ReportingExecutionParameterValue>))]
[JsonSerializable(typeof(IReadOnlyList<ReportingParameterSchemaEntry>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ReportingPublishedDefinitionResponse))]
[JsonSerializable(typeof(IReadOnlyList<ReportingPublishedDefinitionResponse>))]
[JsonSerializable(typeof(Full.NET.Modules.Reporting.Features.ManageExportTasks.ReportingExportAuthorizationSnapshot))]
// OpenAPI 使用端点声明的集合接口类型，数组元数据不能替代集合根类型。
[JsonSerializable(typeof(IReadOnlyList<ReportingGroupResponse>))]
[JsonSerializable(typeof(IReadOnlyList<ReportingQueryPortDefinition>))]
[JsonSerializable(typeof(IReadOnlyList<ReportingDefinitionResponse>))]
[JsonSerializable(typeof(IReadOnlyList<ReportingDefinitionVersionResponse>))]
internal partial class ReportingJsonSerializerContext : JsonSerializerContext;
