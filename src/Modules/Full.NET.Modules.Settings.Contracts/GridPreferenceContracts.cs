namespace Full.NET.Modules.Settings.Contracts;

/// <summary>单个稳定列键的展示偏好。</summary>
public sealed record GridColumnPreference(
    string ColumnKey,
    int Order,
    int? Width,
    bool Visible,
    string? Fixed);

/// <summary>保存当前用户 Grid 偏好的请求。</summary>
public sealed record UpdateGridPreferenceRequest(
    int SchemaVersion,
    IReadOnlyList<GridColumnPreference> Columns,
    int Version);

/// <summary>当前用户 Grid 偏好响应。</summary>
public sealed record GridPreferenceResponse(
    string GridKey,
    int SchemaVersion,
    IReadOnlyList<GridColumnPreference> Columns,
    int Version);
