namespace Full.NET.Modules.Settings.Contracts;

/// <summary>单个稳定列键的展示偏好。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ColumnKey">稳定列键；前端据此与列定义匹配，发布后不可改名。</param>
/// <param name="Order">列在网格中的显示顺序，从 0 开始升序。</param>
/// <param name="Width">列宽（像素）；null 表示使用前端默认宽度。</param>
/// <param name="Visible">该列是否对当前用户可见。</param>
/// <param name="Fixed">固定方向；null 表示不固定，"left"/"right" 分别固定到左右两侧。</param>
public sealed record GridColumnPreference(
    string ColumnKey,
    int Order,
    int? Width,
    bool Visible,
    string? Fixed);

/// <summary>保存当前用户 Grid 偏好的请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="SchemaVersion">偏好结构版本；服务端据此选择反序列化与兼容策略。</param>
/// <param name="Columns">当前用户保存的列偏好集合；顺序即显示顺序。</param>
/// <param name="Version">乐观并发版本号；与服务端不一致时拒绝写入。</param>
public sealed record UpdateGridPreferenceRequest(
    int SchemaVersion,
    IReadOnlyList<GridColumnPreference> Columns,
    int Version);

/// <summary>当前用户 Grid 偏好响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="GridKey">稳定网格标识；与页面/列表一一对应，发布后不可改名。</param>
/// <param name="SchemaVersion">偏好结构版本；与请求中的 SchemaVersion 对齐。</param>
/// <param name="Columns">当前用户的列偏好集合；顺序即显示顺序。</param>
/// <param name="Version">乐观并发版本号；下次保存时须回传以避免覆盖冲突。</param>
public sealed record GridPreferenceResponse(
    string GridKey,
    int SchemaVersion,
    IReadOnlyList<GridColumnPreference> Columns,
    int Version);
