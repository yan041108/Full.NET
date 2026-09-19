#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestPermissions
{
    public const string Read = "enterprise_request.enterprise_requests.read";
    public const string Create = "enterprise_request.enterprise_requests.create";
    public const string Update = "enterprise_request.enterprise_requests.update";
    public const string Disable = "enterprise_request.enterprise_requests.disable";
}

public sealed record EnterpriseRequestResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenantId")] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId")] Guid OrganizationUnitId,
    [property: JsonPropertyName("requestNumber")] string RequestNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalAmount"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId")] Guid ApplicantUserId,
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("createdById")] Guid CreatedById,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("updatedById")] Guid? UpdatedById,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc")] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateEnterpriseRequestRequest(
    [property: JsonPropertyName("requestNumber")] string RequestNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalAmount"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId")] Guid ApplicantUserId);

public sealed record UpdateEnterpriseRequestRequest(
    [property: JsonPropertyName("requestNumber")] string RequestNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalAmount"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal TotalAmount,
    [property: JsonPropertyName("applicantUserId")] Guid ApplicantUserId,
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version);

public sealed record DeleteEnterpriseRequestRequest(
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version);

public static class EnterpriseRequestErrorCodes
{
    public const string NotFound =
        "enterprise_request.enterprise_requests.not_found";

    public const string VersionConflict =
        "enterprise_request.enterprise_requests.version_conflict";
}
