using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Persistence;

namespace Full.NET.Modules.Calendar.Features.Knowledge;

/// <summary>固定字段规范摘要；长度前缀隔开正文，防止分隔符或换行制造等价输入。</summary>
internal static class PersonalScheduleKnowledgeProjection
{
    internal static PersonalScheduleKnowledgeSnapshot Capture(PersonalScheduleRecord row)
    {
        if (row.Id == Guid.Empty || row.OwnerUserId == Guid.Empty || row.Version < 1
            || row.Content is not { Length: >= 1 and <= 256 } || row.StartAtUtc > row.EndAtUtc
            || row.Status is not (PersonalScheduleStatuses.Pending or PersonalScheduleStatuses.Completed))
            throw new InvalidDataException("个人日程来源字段不完整。");
        var start = row.StartAtUtc.ToUniversalTime(); var end = row.EndAtUtc.ToUniversalTime();
        var text = string.Join('\n', PersonalScheduleKnowledgeSourceKeys.Kind, PersonalScheduleKnowledgeSourceKeys.Purpose,
            row.Id.ToString("N"), row.TenantId?.ToString("N") ?? "host", row.OwnerUserId.ToString("N"),
            row.Version.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetByteCount(row.Content).ToString(CultureInfo.InvariantCulture),
            row.Content, start.ToString("O", CultureInfo.InvariantCulture), end.ToString("O", CultureInfo.InvariantCulture), row.Status);
        var hash = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text)));
        var proof = new PersonalScheduleKnowledgeProof(PersonalScheduleKnowledgeSourceKeys.Kind,
            PersonalScheduleKnowledgeSourceKeys.Purpose, row.Id, row.TenantId, row.OwnerUserId, row.Version, hash);
        return new(proof, row.Content, start, end, row.Status);
    }

    internal static bool IsValid(PersonalScheduleKnowledgeProof proof) =>
        proof.Kind == PersonalScheduleKnowledgeSourceKeys.Kind && proof.Purpose == PersonalScheduleKnowledgeSourceKeys.Purpose
        && proof.Id != Guid.Empty && proof.TenantId != Guid.Empty && proof.OwnerUserId != Guid.Empty
        && proof.Version > 0 && proof.ProjectionHash is { Length: 64 }
        && proof.ProjectionHash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
