using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.WorkflowReadCapabilities;

/// <summary>能力不是通用外租户下载凭证，必须固定调用、接收作用域和原持有事实。</summary>
internal static class WorkflowFileReadCapabilityPolicy
{
    internal static string ScopeKey(Guid? tenant) => tenant is { } id ? $"tenant:{id:N}" : "host";
    internal static (Guid? Id, string Key) Scope(ICurrentTenant tenant) => tenant.IsHost ? (null, "host") :
        tenant.IsAvailable && tenant.Id is { } id && id != Guid.Empty ? (id, ScopeKey(id)) : throw new TenantContextMissingException("files.tenant_context_required");
    internal static DateTimeOffset Microseconds(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);
    internal static bool FieldKey(string? value) => value is { Length: > 0 and <= 64 } && char.IsAsciiLetter(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-') && value is not ("prototype" or "constructor");
    internal static bool Valid(WorkflowFileReadCapabilityIssue value, Guid? issuer, DateTimeOffset now) =>
        value.CallId != Guid.Empty && value.FileId != Guid.Empty && value.OwnerReferenceId != Guid.Empty && value.RecipientTenantId != Guid.Empty &&
        value.RecipientTenantId != issuer && value.DirectionKey is "input" or "result" &&
        FieldKey(value.FieldKey) && value.ExpiresAtUtc > now;
    internal static bool Valid(WorkflowFileReadCapabilityRecord row, DateTimeOffset now) =>
        row.Id != Guid.Empty && row.CallId != Guid.Empty && row.SourceFileId != Guid.Empty && row.OwnerReferenceId != Guid.Empty &&
        row.IssuerTenantId != Guid.Empty && row.RecipientTenantId != Guid.Empty && row.IssuerScopeKey == ScopeKey(row.IssuerTenantId) &&
        row.RecipientScopeKey == ScopeKey(row.RecipientTenantId) && row.IssuerScopeKey != row.RecipientScopeKey &&
        row.DirectionKey is "input" or "result" && FieldKey(row.FieldKey) && row.StatusKey == "active" && row.RevokedAtUtc is null && row.Revision >= 1 &&
        row.ExpiresAtUtc > now && row.ExpiresAtUtc > row.CreatedAtUtc && row.SizeBytes > 0 &&
        row.ContentHash is { Length: 64 } && row.ContentHash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        row.OriginalFileName is { Length: > 0 and <= 255 } && !row.OriginalFileName.Any(char.IsControl) &&
        row.ContentType is { Length: > 0 and <= 128 } && !row.ContentType.Any(char.IsControl);
    internal static WorkflowFileReadCapability Describe(WorkflowFileReadCapabilityRecord row) => new(row.Id, row.CallId, row.DirectionKey,
        row.FieldKey, row.OriginalFileName, row.ContentType, row.SizeBytes, row.ContentHash, row.ExpiresAtUtc);
}
