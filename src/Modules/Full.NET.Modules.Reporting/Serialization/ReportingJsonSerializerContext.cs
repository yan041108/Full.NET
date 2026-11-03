using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Reporting.Serialization;

/// <summary>Reporting 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PagedResult<ReportingDefinitionSummaryResponse>))]
[JsonSerializable(typeof(PagedResult<ReportingDefinitionVersionSummaryResponse>))]
[JsonSerializable(typeof(PagedResult<ReportingDataSourceListItem>))]
[JsonSerializable(typeof(ReportingDataSourceListItem))]
[JsonSerializable(typeof(ReportingDataSourceResponse))]
[JsonSerializable(typeof(CreateReportingDataSourceRequest))]
[JsonSerializable(typeof(UpdateReportingDataSourceRequest))]
[JsonSerializable(typeof(TestReportingDataSourceResult))]
[JsonSerializable(typeof(ReportingGroupResponse))]
[JsonSerializable(typeof(IReadOnlyList<ReportingGroupResponse>))]
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
[JsonSerializable(typeof(CancelReportingExportTaskRequest))]
[JsonSerializable(typeof(ReportingExportTaskResponse))]
[JsonSerializable(typeof(ReportingExportTaskDetailResponse))]
[JsonSerializable(typeof(PagedResult<ReportingExportTaskResponse>))]
[JsonSerializable(typeof(CursorPage<ReportingExportTaskResponse>))]
[JsonSerializable(typeof(PublishReportingDefinitionRequest))]
[JsonSerializable(typeof(ReportingQueryPortDefinition))]
[JsonSerializable(typeof(IReadOnlyList<ReportingQueryPortDefinition>))]
[JsonSerializable(typeof(ReportingQueryPortParameterDefinition))]
[JsonSerializable(typeof(ReportingParameterSchemaEntry))]
[JsonSerializable(typeof(IReadOnlyList<ReportingExecutionParameterValue>))]
[JsonSerializable(typeof(IReadOnlyList<ReportingParameterSchemaEntry>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(SessionBindingSnapshot))]
[JsonSerializable(typeof(ReportingDefinitionTenantGrantResponse))]
[JsonSerializable(typeof(PagedResult<ReportingDefinitionTenantGrantResponse>))]
[JsonSerializable(typeof(SetReportingDefinitionTenantGrantRequest))]
[JsonSerializable(typeof(ReportingPublishedDefinitionResponse))]
[JsonSerializable(typeof(PagedResult<ReportingPublishedDefinitionResponse>))]
/// <summary>Reporting 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class ReportingJsonSerializerContext : JsonSerializerContext;
