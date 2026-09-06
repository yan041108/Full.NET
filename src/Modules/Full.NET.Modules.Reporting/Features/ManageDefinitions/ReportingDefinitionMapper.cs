using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitions;

/// <summary>报表定义与版本响应映射。</summary>
internal static class ReportingDefinitionMapper
{
    public static ReportingDefinitionResponse MapDefinition(ReportingDefinitionRecord row) =>
        new(
            row.Id,
            row.GroupId,
            row.DataSourceId,
            row.DefinitionKey,
            row.Name,
            row.Description,
            row.QueryPortKey,
            ReportingDefinitionJson.DeserializeParameterSchema(row.ParameterSchemaJson),
            row.LayoutConfigJson,
            row.LatestPublishedVersionNumber,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    public static ReportingDefinitionVersionResponse MapVersion(ReportingDefinitionVersionRecord row) =>
        new(
            row.Id,
            row.DefinitionId,
            row.VersionNumber,
            row.DataSourceId,
            row.QueryPortKey,
            ReportingDefinitionJson.DeserializeParameterSchema(row.ParameterSchemaJson),
            row.LayoutConfigJson,
            row.ChangeNote,
            row.PublishedByUserId,
            row.PublishedAtUtc);
}
