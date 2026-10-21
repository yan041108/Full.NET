using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Security;

/// <summary>短期预览凭据只绑定一次已消费的分享访问及特定任务；每次使用仍检查当前分享策略。</summary>
internal sealed class DocumentSharePreviewGrantService(
    IDataProtectionProvider protection, IClock clock, IOptionsMonitor<DocumentOfficePreviewConversionOptions> options)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Full.NET.Document.SharePreviewGrant.v1");

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(DocumentShareRecord share, Guid taskId, DateTimeOffset? deadline = null)
    {
        var seconds = Math.Clamp(options.CurrentValue.TimeoutSeconds + options.CurrentValue.PollSeconds + 120, 150, 7320);
        var expires = deadline ?? clock.UtcNow.AddSeconds(seconds);
        if (expires > share.ExpireTime) expires = share.ExpireTime;
        var purpose = taskId == Guid.Empty ? "create" : "task";
        var operationId = taskId == Guid.Empty ? Guid.CreateVersion7() : taskId;
        var payload = string.Join('|', share.Id.ToString("N"), share.DocumentId.ToString("N"), operationId.ToString("N"),
            Fingerprint(share), expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), purpose);
        return (_protector.Protect(payload), expires);
    }

    public bool IsValid(string? token, DocumentShareRecord share, Guid taskId)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            return parts.Length == 6 && parts[0] == share.Id.ToString("N") && parts[1] == share.DocumentId.ToString("N")
                && Guid.TryParseExact(parts[2], "N", out var operationId) && operationId != Guid.Empty
                && (taskId == Guid.Empty ? parts[5] == "create" : parts[5] == "task" && operationId == taskId)
                && parts[3] == Fingerprint(share)
                && long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var until)
                && until > clock.UtcNow.ToUnixTimeSeconds();
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { return false; }
    }

    public (Guid Id, DateTimeOffset ExpiresAtUtc)? GetCreationOperation(string? token, DocumentShareRecord share)
    {
        if (!IsValid(token, share, Guid.Empty)) return null;
        try
        {
            var parts = _protector.Unprotect(token!).Split('|');
            return (Guid.ParseExact(parts[2], "N"), DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[4], CultureInfo.InvariantCulture)));
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { return null; }
    }

    private static string Fingerprint(DocumentShareRecord share)
    {
        // Version 与 AccessCount 同时递增，差值只随策略变更递增；其他访问不会误撤销正在执行的预览。
        var policy = string.Join('|', share.DocumentId.ToString("N"), share.PasswordHash ?? "",
            share.ExpireTime.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            share.MaxAccessCount?.ToString(CultureInfo.InvariantCulture) ?? "",
            (share.Version - share.AccessCount).ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policy)));
    }
}
