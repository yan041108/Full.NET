#nullable enable

using System;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal sealed record EnterpriseRequestRecord(
    Guid Id,
    Guid TenantId,
    Guid OrganizationUnitId,
    string RequestNumber,
    string Title,
    string Status,
    decimal TotalAmount,
    Guid ApplicantUserId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedById,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedById,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedById);
