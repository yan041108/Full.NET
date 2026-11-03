#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "enterprise_request.enterprise_requests.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "enterprise_request.enterprise_requests.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "enterprise_request.enterprise_requests.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "enterprise_request.enterprise_requests.disable";
}

public sealed record EnterpriseRequestResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId"), JsonRequired] Guid OrganizationUnitId,
    [property: JsonPropertyName("requestNumber"), JsonRequired] string RequestNumber,
    [property: JsonPropertyName("title"), JsonRequired] string Title,
    [property: JsonPropertyName("status"), JsonRequired] string Status,
    [property: JsonPropertyName("totalAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId"), JsonRequired] Guid ApplicantUserId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("createdAtUtc"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.UtcDateTimeJsonConverter))] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("createdById"), JsonRequired] Guid CreatedById,
    [property: JsonPropertyName("updatedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("updatedById")] Guid? UpdatedById,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateEnterpriseRequestRequest(
    [property: JsonPropertyName("requestNumber"), JsonRequired] string RequestNumber,
    [property: JsonPropertyName("title"), JsonRequired] string Title,
    [property: JsonPropertyName("status"), JsonRequired] string Status,
    [property: JsonPropertyName("totalAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId"), JsonRequired] Guid ApplicantUserId);

public sealed record UpdateEnterpriseRequestRequest(
    [property: JsonPropertyName("requestNumber"), JsonRequired] string RequestNumber,
    [property: JsonPropertyName("title"), JsonRequired] string Title,
    [property: JsonPropertyName("status"), JsonRequired] string Status,
    [property: JsonPropertyName("totalAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId"), JsonRequired] Guid ApplicantUserId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteEnterpriseRequestRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class EnterpriseRequestErrorCodes
{
    public const string NotFound =
        "enterprise_request.enterprise_requests.not_found";

    public const string VersionConflict =
        "enterprise_request.enterprise_requests.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class EnterpriseRequestListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterRequestNumber")] public string? FilterRequestNumber { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterTitle")] public string? FilterTitle { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterStatus")] public string? FilterStatus { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterTotalAmount")] public decimal? FilterTotalAmount { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterApplicantUserId")] public Guid? FilterApplicantUserId { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterRequestNumber", "filterTitle", "filterStatus", "filterTotalAmount", "filterApplicantUserId" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "requestNumber" or "title" or "status" or "totalAmount" or "applicantUserId")
            || FilterRequestNumber?.Length > 64
            || FilterTitle?.Length > 200
            || FilterStatus?.Length > 32
            || FilterApplicantUserId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("enterprise_request.enterprise_requests.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {
        "filterTotalAmount" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_RequestNumber"] = Pattern(FilterRequestNumber),
            ["Query_Title"] = Pattern(FilterTitle),
            ["Query_Status"] = Pattern(FilterStatus),
            ["Query_TotalAmount"] = FilterTotalAmount,
            ["Query_ApplicantUserId"] = FilterApplicantUserId
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}

/// <summary>外键由已授权主端赋值，不接受调用者提供其他主端标识。</summary>
public sealed record EnterpriseRequestDetailInput(
    [property: JsonPropertyName("lineNumber"), JsonRequired] int LineNumber,
    [property: JsonPropertyName("itemDescription"), JsonRequired] string ItemDescription,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Quantity,
    [property: JsonPropertyName("unitPrice"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal UnitPrice,
    [property: JsonPropertyName("lineAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal LineAmount);
public sealed record EnterpriseRequestDetailEdit(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonRequired] EnterpriseRequestDetailInput Input);
public sealed record EnterpriseRequestDetailRemoval(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);
public sealed record CreateEnterpriseRequestAggregateRequest(
    [property: JsonRequired] CreateEnterpriseRequestRequest Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<EnterpriseRequestDetailInput> Details);
public sealed record UpdateEnterpriseRequestAggregateRequest(
    [property: JsonRequired] UpdateEnterpriseRequestRequest Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<EnterpriseRequestDetailInput> Added,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<EnterpriseRequestDetailEdit> Updated,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<EnterpriseRequestDetailRemoval> Removed);
public sealed record EnterpriseRequestAggregateResponse(
    [property: JsonRequired] EnterpriseRequestResponse Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<EnterpriseRequestDetailResponse> Details);
