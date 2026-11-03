namespace Full.NET.Modules.Identity.Configuration;

/// <summary>API 与 Migrator 共用同一配置门禁，避免初始化出与运行时不同的回调或客户端。</summary>
internal static class IdentityBrowserOptionsValidation
{
    internal static void Validate(IdentityBrowserOptions browser, IdentityOidcOptions oidc)
    {
        if (!browser.Enable) return;
        if (!oidc.Enable || !Uri.TryCreate(browser.PublicOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || !string.IsNullOrEmpty(origin.UserInfo)
            || browser.PublicOrigin != origin.GetLeftPart(UriPartial.Authority)
            || string.IsNullOrWhiteSpace(browser.ClientId) || string.IsNullOrWhiteSpace(browser.ClientSecret)
            || browser.SessionHours is < 1 or > 24)
            throw new InvalidOperationException("Identity:Browser requires enabled OIDC, an exact HTTPS PublicOrigin, confidential credentials and SessionHours 1..24.");
        foreach (var key in new[] { browser.StateSigningKeyBase64, browser.StateEncryptionKeyBase64 })
            if (!Convert.TryFromBase64String(key, new byte[32], out var count) || count != 32)
                throw new InvalidOperationException("Browser state keys must be independent persistent 256-bit Base64 keys.");
        // 比较实际材料而非 Base64 文本，空白等编码差异不能绕过职责分离。
        var signing = Convert.FromBase64String(browser.StateSigningKeyBase64);
        var encryption = Convert.FromBase64String(browser.StateEncryptionKeyBase64);
        var protocol = string.IsNullOrEmpty(oidc.EncryptionKeyBase64) ? [] : Convert.FromBase64String(oidc.EncryptionKeyBase64);
        if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(signing, encryption)
            || System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(signing, protocol)
            || System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(encryption, protocol))
            throw new InvalidOperationException("Browser and authorization server protection keys must be distinct.");
        if (oidc.Clients.Any(client => client.ClientId == browser.ClientId))
            throw new InvalidOperationException("Configure the browser client through Identity:Browser only.");
    }
}
