namespace Full.NET.Modules.Settings.Contracts;

/// <summary>字典项列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Value 发布后不可改名；DisplayOrder 仅控制展示顺序，不影响业务匹配。</remarks>
/// <param name="Id">字典项逻辑主键（应用端 UUID v7）。</param>
/// <param name="DictTypeId">所属字典类型标识；与字典项一一对应。</param>
/// <param name="Label">展示名称；用于前端文案。</param>
/// <param name="Value">稳定机器值；发布后不可改名，业务匹配依据此字段。</param>
/// <param name="Color">展示颜色；无指定时为 <see langword="null"/>。</param>
/// <param name="DisplayOrder">展示排序；升序排列，仅影响前端顺序。</param>
/// <param name="IsActive">是否启用；停用项不进入业务匹配集合。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观锁版本；用于并发控制。</param>
public sealed record DictItemResponse(
    Guid Id,
    Guid DictTypeId,
    string Label,
    string Value,
    string? Color,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>在指定字典类型下创建字典项请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Value 在同一字典类型下唯一，发布后不可改名。</remarks>
/// <param name="Label">展示名称。</param>
/// <param name="Value">稳定机器值；同一字典类型下唯一，发布后不可改名。</param>
/// <param name="Color">展示颜色；无指定时为 <see langword="null"/>。</param>
/// <param name="DisplayOrder">展示排序；升序排列。</param>
public sealed record CreateDictItemRequest(
    string Label,
    string Value,
    string? Color,
    int DisplayOrder);

/// <summary>更新字典项请求；稳定值创建后不可变。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Value 不在此请求中更新；Version 必须匹配当前持久化版本以通过乐观并发。</remarks>
/// <param name="Label">展示名称。</param>
/// <param name="Color">展示颜色；传 <see langword="null"/> 表示清除颜色。</param>
/// <param name="DisplayOrder">展示排序；升序排列。</param>
/// <param name="Version">乐观锁版本；必须匹配当前持久化版本，否则并发冲突。</param>
public sealed record UpdateDictItemRequest(
    string Label,
    string? Color,
    int DisplayOrder,
    int Version);

/// <summary>硬删除字典项请求；携带乐观锁版本用于并发控制。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。硬删除不可恢复，已发布 Value 删除后不得复用。</remarks>
/// <param name="Version">乐观锁版本；必须匹配当前持久化版本，否则并发冲突。</param>
public sealed record DeleteDictItemRequest(int Version);
