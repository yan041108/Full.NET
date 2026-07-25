namespace Full.NET.Modules.Settings.Contracts;

/// <summary>字典项列表项与详情响应。</summary>
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
public sealed record CreateDictItemRequest(
    string Label,
    string Value,
    string? Color,
    int DisplayOrder);

/// <summary>更新字典项请求；稳定值创建后不可变。</summary>
public sealed record UpdateDictItemRequest(
    string Label,
    string? Color,
    int DisplayOrder,
    int Version);
