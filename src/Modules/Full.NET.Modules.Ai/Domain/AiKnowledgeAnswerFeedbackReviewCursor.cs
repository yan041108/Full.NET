using Full.NET.Abstractions.Time;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>游标只承载加密扫描位置，绑定评审者和可信租户，不能作为来源授权。</summary>
internal sealed class AiKnowledgeAnswerFeedbackReviewCursor(IDataProtectionProvider provider, IClock clock)
{
    private readonly IDataProtector protector = provider.CreateProtector("Full.NET.Ai.KnowledgeAnswerFeedback.ReviewCursor.v1");
    internal string Protect(Guid position, Guid actor, Guid? tenant) => protector.Protect(string.Join('|', "1",
        actor.ToString("N"), tenant?.ToString("N") ?? "host", position.ToString("N"),
        clock.UtcNow.AddMinutes(15).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));

    internal Guid? Read(string cursor, Guid actor, Guid? tenant)
    {
        if (actor == Guid.Empty || tenant == Guid.Empty || cursor.Length is < 1 or > 2048) return null;
        try
        {
            var parts = protector.Unprotect(cursor).Split('|');
            if (parts.Length != 5 || parts[0] != "1" || parts[1] != actor.ToString("N") || parts[2] != (tenant?.ToString("N") ?? "host")
                || !Guid.TryParseExact(parts[3], "N", out var position) || position == Guid.Empty
                || !long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)) return null;
            var now = clock.UtcNow.ToUnixTimeSeconds();
            return expiry > now && expiry <= now + 900 ? position : null;
        }
        catch (CryptographicException) { return null; }
    }
}
