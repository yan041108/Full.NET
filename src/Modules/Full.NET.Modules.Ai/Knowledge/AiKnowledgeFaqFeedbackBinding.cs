using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>服务器冻结的私人回答资格；正文及原用户问题不进入来源 JSON。</summary>
internal sealed record AiKnowledgeFaqFeedbackBinding(Guid FeedbackId, int FeedbackVersion, Guid ReviewId, int ReviewVersion,
    Guid MessageId, Guid SessionId, Guid OwnerUserId, Guid? TenantId, string LineageHash, string LineageSqlServerHash,
    string AnswerHash, string AnswerSqlServerHash)
{
    internal static string UnicodeHash(string value) => Convert.ToHexString(SHA256.HashData(new UnicodeEncoding(false, false, true).GetBytes(value)));
    internal void Validate()
    {
        if (new[] { FeedbackId, ReviewId, MessageId, SessionId, OwnerUserId }.Any(id => id == Guid.Empty)
            || TenantId == Guid.Empty || FeedbackVersion < 1 || ReviewVersion < 1
            || new[] { LineageHash, LineageSqlServerHash, AnswerHash, AnswerSqlServerHash }.Any(hash => hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("反馈来源绑定无效。");
    }
    internal AiKnowledgeFaqFeedbackSource Public() => new(FeedbackId, FeedbackVersion, ReviewId, ReviewVersion, MessageId, LineageHash, AnswerHash);
}
