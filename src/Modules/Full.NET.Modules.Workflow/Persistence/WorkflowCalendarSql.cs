using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>版本通过所属日历继承可信作用域，列表有界，草稿和发布头按修订推进。</summary>
internal static class WorkflowCalendarSql
{
    private const string CalendarProjection="calendar.Id,calendar.TenantId,calendar.ScopeKey,calendar.TenantScopeKey,calendar.CalendarKey,calendar.DisplayName,calendar.DraftJson,calendar.Revision,calendar.LatestVersionNumber,calendar.CreatedById,calendar.UpdatedById,calendar.CreatedAtUtc,calendar.UpdatedAtUtc,version.Id AS LatestVersionId";
    private const string SummaryProjection="calendar.Id,calendar.CalendarKey,calendar.DisplayName,calendar.Revision,calendar.LatestVersionNumber,version.Id AS LatestVersionId,version.TimeZoneKey,calendar.CreatedAtUtc,calendar.UpdatedAtUtc";
    private const string CalendarFrom=" FROM fn_workflow_calendar AS calendar LEFT JOIN fn_workflow_calendar_version AS version ON version.CalendarId=calendar.Id AND version.VersionNumber=calendar.LatestVersionNumber ";
    private const string ListWhere=" WHERE calendar.TenantScopeKey=@TenantScopeKey AND (@HasAfter=0 OR calendar.CreatedAtUtc<@AfterCreatedAtUtc OR (calendar.CreatedAtUtc=@AfterCreatedAtUtc AND calendar.Id<@AfterId)) ORDER BY calendar.CreatedAtUtc DESC,calendar.Id DESC ";
    private const string VersionProjection="version.Id,version.CalendarId,version.VersionNumber,version.CanonicalJson,version.ContentHash,version.TimeZoneKey,version.PublishedById,version.PublishedAtUtc,calendar.TenantScopeKey";
    private const string VersionSummaryProjection="version.Id,version.CalendarId,version.VersionNumber,version.ContentHash,version.TimeZoneKey,version.PublishedAtUtc";
    private const string VersionFrom=" FROM fn_workflow_calendar_version AS version INNER JOIN fn_workflow_calendar AS calendar ON calendar.Id=version.CalendarId ";
    private const string VersionsWhere=" WHERE calendar.TenantScopeKey=@TenantScopeKey AND calendar.Id=@CalendarId AND version.VersionNumber<@BeforeVersionNumber ORDER BY version.VersionNumber DESC ";
    public static readonly SqlStatement Find=new("workflow.calendar.find","SELECT "+CalendarProjection+CalendarFrom+" WHERE calendar.Id=@Id AND calendar.TenantScopeKey=@TenantScopeKey",SqlDataScope.Global);
    public static readonly SqlStatement ListSqlServer=new("workflow.calendar.list.sqlserver","SELECT TOP (@Take) "+SummaryProjection+CalendarFrom+ListWhere,SqlDataScope.Global);
    public static readonly SqlStatement ListMySql=new("workflow.calendar.list.mysql","SELECT "+SummaryProjection+CalendarFrom+ListWhere+" LIMIT @Take",SqlDataScope.Global);
    public static readonly SqlStatement FindVersion=new("workflow.calendar_version.find","SELECT "+VersionProjection+VersionFrom+" WHERE version.Id=@Id AND calendar.TenantScopeKey=@TenantScopeKey",SqlDataScope.Global);
    public static readonly SqlStatement FindVersionsByIds=new("workflow.calendar_version.find_ids","SELECT "+VersionProjection+VersionFrom+" WHERE version.Id IN @Ids AND calendar.TenantScopeKey=@TenantScopeKey",SqlDataScope.Global);
    public static readonly SqlStatement ListVersionsSqlServer=new("workflow.calendar_version.list.sqlserver","SELECT TOP (@Take) "+VersionSummaryProjection+VersionFrom+VersionsWhere,SqlDataScope.Global);
    public static readonly SqlStatement ListVersionsMySql=new("workflow.calendar_version.list.mysql","SELECT "+VersionSummaryProjection+VersionFrom+VersionsWhere+" LIMIT @Take",SqlDataScope.Global);
    public static readonly SqlStatement Insert=new("workflow.calendar.insert","""
        INSERT INTO fn_workflow_calendar (Id,TenantId,ScopeKey,TenantScopeKey,CalendarKey,DisplayName,DraftJson,Revision,LatestVersionNumber,CreatedById,UpdatedById,CreatedAtUtc,UpdatedAtUtc)
        VALUES (@Id,@TenantId,@ScopeKey,@TenantScopeKey,@CalendarKey,@DisplayName,@DraftJson,1,NULL,@ActorId,@ActorId,@Now,@Now)
        """,SqlDataScope.Global);
    public static readonly SqlStatement UpdateDraft=new("workflow.calendar.update_draft","""
        UPDATE fn_workflow_calendar SET DisplayName=@DisplayName,DraftJson=@DraftJson,Revision=Revision+1,UpdatedById=@ActorId,UpdatedAtUtc=@Now
        WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """,SqlDataScope.Global);
    public static readonly SqlStatement Publish=new("workflow.calendar.publish","""
        UPDATE fn_workflow_calendar SET LatestVersionNumber=@VersionNumber,Revision=Revision+1,UpdatedById=@ActorId,UpdatedAtUtc=@Now
        WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """,SqlDataScope.Global);
    public static readonly SqlStatement Lock=new("workflow.calendar.lock","""
        UPDATE fn_workflow_calendar SET Revision=Revision
        WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """,SqlDataScope.Global);
    public static readonly SqlStatement InsertVersion=new("workflow.calendar_version.insert","""
        INSERT INTO fn_workflow_calendar_version (Id,CalendarId,VersionNumber,CanonicalJson,ContentHash,TimeZoneKey,PublishedById,PublishedAtUtc)
        SELECT @Id,@CalendarId,@VersionNumber,@CanonicalJson,@ContentHash,@TimeZoneKey,@ActorId,@Now
        FROM fn_workflow_calendar AS calendar WHERE calendar.Id=@CalendarId AND calendar.TenantScopeKey=@TenantScopeKey
        """,SqlDataScope.Global);
}

/// <summary>日历草稿及当前发布头，末列由自有版本关联取得。</summary>
internal sealed record WorkflowCalendarRecord(Guid Id,Guid? TenantId,string ScopeKey,string TenantScopeKey,string CalendarKey,string DisplayName,string DraftJson,
    long Revision,int? LatestVersionNumber,Guid CreatedById,Guid UpdatedById,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc,Guid? LatestVersionId);
/// <summary>分页只读取管理摘要，不批量携带完整工作窗口和时区规则。</summary>
internal sealed record WorkflowCalendarSummaryRecord(Guid Id,string CalendarKey,string DisplayName,long Revision,int? LatestVersionNumber,Guid? LatestVersionId,
    string? TimeZoneKey,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc);
/// <summary>版本的作用域从所属日历权威继承，不能独立伪造。</summary>
internal sealed record WorkflowCalendarVersionRecord(Guid Id,Guid CalendarId,int VersionNumber,string CanonicalJson,string ContentHash,string TimeZoneKey,
    Guid PublishedById,DateTimeOffset PublishedAtUtc,string TenantScopeKey);
/// <summary>历史版本分页仅提供选择所需的稳定版本和摘要。</summary>
internal sealed record WorkflowCalendarVersionSummaryRecord(Guid Id,Guid CalendarId,int VersionNumber,string ContentHash,string TimeZoneKey,DateTimeOffset PublishedAtUtc);
