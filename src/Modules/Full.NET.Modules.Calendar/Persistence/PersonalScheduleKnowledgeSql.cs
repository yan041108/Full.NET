using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>来源所有者的有界批量读取；主键集合不能替代本人和可信租户过滤。</summary>
internal static class PersonalScheduleKnowledgeSql
{
    private const string Columns = """
        Id, TenantId, OwnerUserId, Content, StartAtUtc, EndAtUtc, Status,
        CompletedAtUtc, CreatedAtUtc, UpdatedAtUtc, Version
        """;

    internal static readonly SqlStatement ReadHost = new(
        "calendar.read_personal_schedule_knowledge.host",
        $"""
        SELECT {Columns} FROM fn_calendar_personal_schedule
        WHERE TenantId IS NULL AND OwnerUserId = @OwnerUserId AND Id IN @Ids
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement ReadTenant = new(
        "calendar.read_personal_schedule_knowledge.tenant",
        $"""
        SELECT {Columns} FROM fn_calendar_personal_schedule
        WHERE TenantId = @TenantId AND OwnerUserId = @OwnerUserId AND Id IN @Ids
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
