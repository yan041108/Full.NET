using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>加载并校验 API Key 哈希，构造可授权主体。</summary>
internal sealed class ApiKeyAuthenticationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    private static readonly TimeSpan LastUsedObservationWindow =
        TimeSpan.FromMinutes(5);

    public async Task<ClaimsPrincipal?> AuthenticateAsync(
        string secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var keyHash = TokenHash.Compute(secret);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ApiKeyAuthenticationRow>(
                ApiKeySql.FindForAuthentication,
                IdentitySqlParameters.Create(("KeyHash", keyHash)),
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        if (!IsActive(row, now))
        {
            return null;
        }

        var permissions = DeserializePermissions(row!.PermissionsJson);
        if (permissions.Count == 0)
        {
            return null;
        }

        var lastUsedBeforeUtc = now - LastUsedObservationWindow;
        if (row.LastUsedAtUtc is null || row.LastUsedAtUtc <= lastUsedBeforeUtc)
        {
            await commandExecutor.ExecuteAsync(
                    ApiKeySql.TouchLastUsed,
                    IdentitySqlParameters.Create(
                        ("ApiKeyId", row.ApiKeyId),
                        ("LastUsedAtUtc", now),
                        ("LastUsedBeforeUtc", lastUsedBeforeUtc)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, row.UserId.ToString("D")),
            new(JwtRegisteredClaimNames.Name, row.DisplayName),
            new("preferred_username", row.Username),
            new(IdentityClaimTypes.ActorScope, "host"),
            new(IdentityClaimTypes.Scope, "host"),
            new(IdentityClaimTypes.SecurityStamp, row.SecurityStamp),
            new(IdentityClaimTypes.ApiKeyId, row.ApiKeyId.ToString("D")),
        };
        foreach (var permission in permissions)
        {
            claims.Add(new Claim(IdentityClaimTypes.Permission, permission));
        }

        var identity = new ClaimsIdentity(
            claims,
            ApiKeyAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static bool IsActive(
        ApiKeyAuthenticationRow? row,
        DateTimeOffset now) =>
        row is not null
        && row.IsActive
        && row.UserIsActive
        && !(row.UserLockoutEndUtc > now)
        && (row.ExpiresAtUtc is null || row.ExpiresAtUtc > now);

    internal static IReadOnlyList<string> DeserializePermissions(string permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(
                    permissionsJson,
                    IdentityJsonSerializerContext.Default.StringArray)
                ?.Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string SerializePermissions(IReadOnlyList<string> permissions) =>
        JsonSerializer.Serialize(
            permissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToArray(),
            IdentityJsonSerializerContext.Default.StringArray);
}
