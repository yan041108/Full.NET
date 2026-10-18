using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>短期加密候选绑定原登录、来源及审核版本；读取凭据不代替实时来源授权。</summary>
internal sealed class AiFeedbackEvaluationToken(IDataProtectionProvider provider, IClock clock)
{
    // 三段正文最多14336个UTF-16字符，默认JSON编码最坏六倍转义；另为固定身份元数据预留空间。
    private const int MaximumJsonLength = 96 * 1024;
    private const int MaximumTokenLength = 128 * 1024;
    private readonly IDataProtector protector = provider.CreateProtector("Full.NET.Ai.FeedbackEvaluation.Candidate.v1");
    internal AiFeedbackEvaluationCandidate Protect(AiFeedbackEvaluationEnvelope envelope)
    {
        if (!Valid(envelope)) throw new ArgumentException("Invalid feedback evaluation candidate.", nameof(envelope));
        var json = JsonSerializer.Serialize(envelope, AiJsonSerializerContext.Default.AiFeedbackEvaluationEnvelope);
        if (json.Length > MaximumJsonLength) throw new ArgumentException("Feedback evaluation candidate is too large.", nameof(envelope));
        var token = protector.Protect(json);
        if (token.Length > MaximumTokenLength) throw new ArgumentException("Feedback evaluation candidate is too large.", nameof(envelope));
        return new(token, AiFeedbackEvaluationPolicy.Hash(envelope.Sample), envelope.ExpiresAtUtc);
    }
    internal AiFeedbackEvaluationEnvelope? Read(string? value, Guid actor, Guid? tenant, Guid session)
    {
        if (value is not { Length: > 0 and <= MaximumTokenLength } || actor == Guid.Empty || tenant == Guid.Empty || session == Guid.Empty) return null;
        try
        {
            var json = protector.Unprotect(value);
            if (json.Length > MaximumJsonLength) return null;
            var envelope = JsonSerializer.Deserialize(json, AiJsonSerializerContext.Default.AiFeedbackEvaluationEnvelope);
            return envelope is not null && Valid(envelope) && envelope.ActorUserId == actor && envelope.TenantId == tenant
                && envelope.SessionId == session ? envelope : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
    private bool Valid(AiFeedbackEvaluationEnvelope value) => value.FormatVersion == 1 && value.ActorUserId != Guid.Empty
        && value.TenantId != Guid.Empty && value.SessionId != Guid.Empty && value.FeedbackId != Guid.Empty
        && value.FeedbackVersion > 0 && value.ReviewVersion > 0 && value.SourceBindingHash.Length == 64
        && value.ExpiresAtUtc > clock.UtcNow && value.ExpiresAtUtc <= clock.UtcNow.AddMinutes(15)
        && AiFeedbackEvaluationPolicy.Valid(value.Sample);
}

/// <summary>凭据内的来源摘要保留完整依赖绑定；不向导出样例释放生产身份。</summary>
internal sealed record AiFeedbackEvaluationEnvelope(int FormatVersion, Guid ActorUserId, Guid? TenantId, Guid SessionId,
    Guid FeedbackId, int FeedbackVersion, int ReviewVersion, string SourceBindingHash, AiFeedbackEvaluationSample Sample, DateTimeOffset ExpiresAtUtc);
