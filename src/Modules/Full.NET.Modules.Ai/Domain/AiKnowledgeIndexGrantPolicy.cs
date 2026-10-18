using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Serialization;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>授权边界固定且保守记账，未知或失败不会自动恢复授权额度。</summary>
internal static class AiKnowledgeIndexGrantPolicy
{
    internal static bool Valid(CreateAiKnowledgeIndexGrantRequest request, DateTimeOffset now) => request.ConfirmPaidDispatch
        && request.ClientRequestId != Guid.Empty && request.KnowledgeBaseVersion > 0 && request.DocumentLimit is >= 1 and <= 64
        && request.InputTokenLimitPerDocument is >= 1 and <= 1_000_000
        && request.ExpiresAtUtc >= now.AddMinutes(1) && request.ExpiresAtUtc <= now.AddHours(24);

    internal static SessionBindingSnapshot? Session(AiKnowledgeIndexGrantRecord row)
    {
        if (row.SessionJson.Length is < 1 or > 4096 || row.OwnerUserId == Guid.Empty || row.TenantId == Guid.Empty) return null;
        try
        {
            var session = JsonSerializer.Deserialize(row.SessionJson, AiJsonSerializerContext.Default.SessionBindingSnapshot);
            return session is not null && session.UserId == row.OwnerUserId && session.TenantId == row.TenantId && session.SessionId != Guid.Empty
                && session.SessionKind is SessionBindingKinds.Refresh or SessionBindingKinds.OidcApplication
                && Text(session.SecurityStamp, 128) && Text(session.ActorScope, 64) && Text(session.EffectiveScope, 64) ? session : null;
        }
        catch (JsonException) { return null; }
    }
    internal static bool Live(AiKnowledgeIndexGrantRecord row, DateTimeOffset now) => row.Id != Guid.Empty && !row.IsRevoked
        && row.ExpiresAtUtc > now && row.Version > 0 && row.KnowledgeBaseVersion > 0 && row.ModelConfigId != Guid.Empty
        && row.ModelConfigVersion > 0 && row.DocumentLimit is >= 1 and <= 64 && row.AdmittedDocuments >= 0 && row.AdmittedDocuments <= row.DocumentLimit
        && row.InputTokenLimitPerDocument is >= 1 and <= 1_000_000 && row.ReservedInputTokens >= 0
        && row.ReservedInputTokens <= row.InputTokenLimitPerDocument && row.LastBatchOrdinal is >= -1 and < 4096;
    private static bool Text(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit && !string.IsNullOrWhiteSpace(value);
}
