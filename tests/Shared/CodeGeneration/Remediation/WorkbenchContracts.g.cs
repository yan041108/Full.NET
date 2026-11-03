#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class WorkbenchPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.workbenches.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.workbenches.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.workbenches.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.workbenches.disable";
}

public sealed record WorkbenchResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Quantity,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("isReady")] bool? IsReady,
    [property: JsonPropertyName("referenceId")] Guid? ReferenceId,
    [property: JsonPropertyName("occurredAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? OccurredAtUtc,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateWorkbenchRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Quantity,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("isReady")] bool? IsReady,
    [property: JsonPropertyName("referenceId")] Guid? ReferenceId,
    [property: JsonPropertyName("occurredAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? OccurredAtUtc);

public sealed record UpdateWorkbenchRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Quantity,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("isReady")] bool? IsReady,
    [property: JsonPropertyName("referenceId")] Guid? ReferenceId,
    [property: JsonPropertyName("occurredAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? OccurredAtUtc,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteWorkbenchRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class WorkbenchErrorCodes
{
    public const string NotFound =
        "catalog.workbenches.not_found";

    public const string VersionConflict =
        "catalog.workbenches.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class WorkbenchListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterName")] public string? FilterName { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterQuantity")] public long? FilterQuantity { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterPriceFrom")] public decimal? FilterPriceFrom { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterPriceTo")] public decimal? FilterPriceTo { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterIsReady")] public bool? FilterIsReady { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterReferenceId")] public Guid? FilterReferenceId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterOccurredAtUtcFrom")] public DateTimeOffset? FilterOccurredAtUtcFrom { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterOccurredAtUtcTo")] public DateTimeOffset? FilterOccurredAtUtcTo { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterName", "filterQuantity", "filterPriceFrom", "filterPriceTo", "filterIsReady", "filterReferenceId", "filterOccurredAtUtcFrom", "filterOccurredAtUtcTo" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "name" or "quantity" or "price" or "isReady" or "referenceId" or "occurredAtUtc")
            || FilterName?.Length > 64
            || FilterReferenceId == Guid.Empty
            || (FilterPriceFrom is not null && FilterPriceTo is not null && FilterPriceFrom > FilterPriceTo)
            || (FilterOccurredAtUtcFrom is not null && FilterOccurredAtUtcTo is not null && FilterOccurredAtUtcFrom > FilterOccurredAtUtcTo)
            || (FilterOccurredAtUtcFrom is DateTimeOffset FilterOccurredAtUtcFromDate && (FilterOccurredAtUtcFromDate.ToUniversalTime().Year < 1000 || FilterOccurredAtUtcFromDate.UtcTicks % 10 != 0))
            || (FilterOccurredAtUtcTo is DateTimeOffset FilterOccurredAtUtcToDate && (FilterOccurredAtUtcToDate.ToUniversalTime().Year < 1000 || FilterOccurredAtUtcToDate.UtcTicks % 10 != 0)) ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.workbenches.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {
        "filterQuantity" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryInt64(value, out _),
        "filterPriceFrom" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        "filterPriceTo" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        "filterOccurredAtUtcFrom" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryUtcDateTime(value, out var FilterOccurredAtUtcFromDate) && FilterOccurredAtUtcFromDate.Year >= 1000 && FilterOccurredAtUtcFromDate.UtcTicks % 10 == 0,
        "filterOccurredAtUtcTo" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryUtcDateTime(value, out var FilterOccurredAtUtcToDate) && FilterOccurredAtUtcToDate.Year >= 1000 && FilterOccurredAtUtcToDate.UtcTicks % 10 == 0,
        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_Name"] = Pattern(FilterName),
            ["Query_Quantity"] = FilterQuantity,
            ["Query_PriceFrom"] = FilterPriceFrom,
            ["Query_PriceTo"] = FilterPriceTo,
            ["Query_IsReady"] = FilterIsReady,
            ["Query_ReferenceId"] = FilterReferenceId,
            ["Query_OccurredAtUtcFrom"] = FilterOccurredAtUtcFrom?.ToUniversalTime(),
            ["Query_OccurredAtUtcTo"] = FilterOccurredAtUtcTo?.ToUniversalTime()
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
