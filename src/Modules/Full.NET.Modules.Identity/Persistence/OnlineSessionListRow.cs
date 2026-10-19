/// <summary>Identity 持久化相关类型；仅供 Dapper 或内部编排使用。</summary>
namespace Full.NET.Modules.Identity.Persistence;

internal sealed class OnlineSessionListRow
{
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public Guid? ActiveTenantId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}

internal sealed class OnlineSessionRevokeRow
{
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public Guid? ActiveTenantId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}
