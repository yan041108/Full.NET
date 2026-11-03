using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>和旧准入凭据分离的窄用途准备，不允许把无原答案绑定的旧 token 直接持久化。</summary>
internal sealed class AiKnowledgeEvaluationCandidateToken(IDataProtectionProvider provider, IClock clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("Full.NET.Ai.KnowledgeEvaluation.Persistence.v1");
    internal AiKnowledgeEvaluationPreparedFeedback Protect(AiKnowledgeEvaluationCandidateEnvelope value)
    {
        if (!Valid(value)) throw new InvalidDataException();
        var json = JsonSerializer.Serialize(value, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationCandidateEnvelope);
        if (Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new InvalidDataException();
        var token = protector.Protect(json);
        if (token.Length > 384 * 1024) throw new InvalidDataException();
        return new(token, AiKnowledgeEvaluationPolicy.Hash(value.SourceJson), AiFeedbackEvaluationPolicy.Hash(value.Sample), value.ExpiresAtUtc);
    }
    internal AiKnowledgeEvaluationCandidateEnvelope? Read(string? token, Guid actor, Guid? tenant, Guid session, Guid knowledge)
    {
        if (token is not { Length: > 0 and <= 384 * 1024 }) return null;
        try
        {
            var json = protector.Unprotect(token);
            if (Encoding.UTF8.GetByteCount(json) > 256 * 1024) return null;
            var value = JsonSerializer.Deserialize(json, AiKnowledgeEvaluationJsonContext.Default.AiKnowledgeEvaluationCandidateEnvelope);
            return value is not null && Valid(value) && value.ActorUserId == actor && value.TenantId == tenant
                && value.SessionId == session && value.KnowledgeBaseId == knowledge ? value : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or InvalidDataException or ArgumentException) { return null; }
    }
    private bool Valid(AiKnowledgeEvaluationCandidateEnvelope value) => value.Version == 1 && value.ActorUserId != Guid.Empty
        && value.TenantId != Guid.Empty && value.SessionId != Guid.Empty && value.KnowledgeBaseId != Guid.Empty && value.KnowledgeVersion > 0
        && value.ExpiresAtUtc > clock.UtcNow && value.ExpiresAtUtc <= clock.UtcNow.AddMinutes(15)
        && AiFeedbackEvaluationPolicy.Valid(value.Sample) && AiKnowledgeEvaluationPolicy.Origin(value.SourceJson).Feedback?.TenantId == value.TenantId;
}

/// <summary>只在加密准备中短暂携带；落库时存来源足迹，不保存 token 或原生产答案。</summary>
internal sealed record AiKnowledgeEvaluationCandidateEnvelope(int Version, Guid ActorUserId, Guid? TenantId, Guid SessionId,
    Guid KnowledgeBaseId, int KnowledgeVersion, string SourceJson, AiFeedbackEvaluationSample Sample, DateTimeOffset ExpiresAtUtc);
