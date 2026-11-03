using System.Text.Json.Serialization;

namespace Full.NET.Modules.Settings.Contracts;

/// <summary>查询方案自己的精确资格；不能代替业务列表的读取和数据权限。</summary>
public static class QuerySchemePermissions
{
    /// <summary>读取本人和当前上下文已共享的方案。</summary>
    public const string Read = "settings.query_schemes.read";
    /// <summary>创建本人方案。</summary>
    public const string Create = "settings.query_schemes.create";
    /// <summary>修改本人方案。</summary>
    public const string Update = "settings.query_schemes.update";
    /// <summary>删除本人方案。</summary>
    public const string Delete = "settings.query_schemes.delete";
    /// <summary>修改当前用户和上下文的默认选择。</summary>
    public const string SetDefault = "settings.query_schemes.set_default";
    /// <summary>在当前租户共享或撤销本人方案。</summary>
    public const string Share = "settings.query_schemes.share";
}

/// <summary>业务模块允许的包含筛选字段；值始终为有界字面文本，不承载 SQL。</summary>
/// <param name="FieldKey">稳定业务字段键。</param>
/// <param name="MaxLength">筛选值最大字符数。</param>
public sealed record QuerySchemeFilterDefinition(string FieldKey, int MaxLength);

/// <summary>由业务所有者静态发布的查询 Schema；Settings 只验证条件，不执行业务查询。</summary>
/// <param name="GridKey">归属业务的稳定 Grid 键。</param>
/// <param name="SchemaVersion">当前唯一可执行结构版本。</param>
/// <param name="ReadPermission">业务列表的精确读取权限。</param>
/// <param name="SupportsHost">该业务列表是否真正支持 Host。</param>
/// <param name="SupportsTenant">该业务列表是否支持可信租户。</param>
/// <param name="Filters">允许的包含筛选字段。</param>
/// <param name="SortKeys">业务允许的排序键。</param>
/// <param name="ColumnKeys">允许的展示列键。</param>
public sealed record QuerySchemeDefinition(string GridKey, int SchemaVersion, string ReadPermission,
    bool SupportsHost, bool SupportsTenant, IReadOnlyList<QuerySchemeFilterDefinition> Filters,
    IReadOnlyList<string> SortKeys, IReadOnlyList<string> ColumnKeys);

/// <summary>真实业务消费者贡献自己的 Schema，禁止 Settings 反向引用业务实现。</summary>
public interface IQuerySchemeContributor
{
    /// <summary>当前程序集拥有的静态 Schema，不能从请求或通用配置构造。</summary>
    IReadOnlyCollection<QuerySchemeDefinition> Definitions { get; }
}

/// <summary>包含筛选项；当前版本唯一运算符为 contains。</summary>
/// <param name="FieldKey">业务 Schema 发布的字段。</param>
/// <param name="OperatorKey">稳定运算符机器码。</param>
/// <param name="Value">由原业务查询作为字面量解释的有界值。</param>
public sealed record QuerySchemeFilter([property: JsonRequired] string FieldKey, [property: JsonRequired] string OperatorKey, [property: JsonRequired] string Value);

/// <summary>只保存查询条件及展示配置，不保存结果、用户、租户或任意查询字符串。</summary>
/// <param name="Filters">不重复的受控筛选。</param>
/// <param name="SortBy">允许的单个排序键。</param>
/// <param name="Descending">是否降序。</param>
/// <param name="Columns">复用网格列偏好；不授予字段读取权限。</param>
public sealed record QuerySchemeConfiguration([property: JsonRequired] IReadOnlyList<QuerySchemeFilter> Filters, [property: JsonRequired] string SortBy,
    [property: JsonRequired] bool Descending, [property: JsonRequired] IReadOnlyList<GridColumnPreference> Columns);

/// <summary>保存本人方案；新建版本为零，修改必须提供当前版本。</summary>
/// <param name="Name">当前用户定义的有界名称。</param>
/// <param name="SchemaVersion">业务当前 Schema 版本。</param>
/// <param name="Configuration">受控查询条件。</param>
/// <param name="Version">新建为零；更新为已读取的 CAS 版本。</param>
public sealed record SaveQuerySchemeRequest([property: JsonRequired] string Name, [property: JsonRequired] int SchemaVersion, [property: JsonRequired] QuerySchemeConfiguration Configuration, [property: JsonRequired] long Version);

/// <summary>共享资格的显式版本化变更。</summary>
/// <param name="IsShared">是否向当前租户共享。</param>
/// <param name="Version">已读取的方案版本。</param>
public sealed record ShareQuerySchemeRequest([property: JsonRequired] bool IsShared, [property: JsonRequired] long Version);

/// <summary>删除本人方案的版本条件。</summary>
/// <param name="Version">已读取的方案版本。</param>
public sealed record DeleteQuerySchemeRequest([property: JsonRequired] long Version);

/// <summary>不含配置正文的有界列表摘要。</summary>
/// <param name="Id">应用生成的 UUID v7。</param>
/// <param name="Name">用户定义名称。</param>
/// <param name="OwnerUserId">创建主体。</param>
/// <param name="SchemaVersion">保存时的结构版本。</param>
/// <param name="IsShared">是否在当前租户共享。</param>
/// <param name="IsCompatible">保存结构版本是否与当前版本一致；详情还会重验正文。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
/// <param name="UpdatedAtUtc">最后修改时间。</param>
/// <param name="Version">并发版本。</param>
public sealed record QuerySchemeSummary(Guid Id, string Name, Guid OwnerUserId, int SchemaVersion, bool IsShared,
    bool IsCompatible, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, long Version);

/// <summary>可见方案详情；过期或损坏配置仅返回摘要并提示修正。</summary>
/// <param name="Summary">可见方案的最小元数据。</param>
/// <param name="Configuration">通过当前业务 Schema 校验的配置；不可执行时为空。</param>
public sealed record QuerySchemeDetail(QuerySchemeSummary Summary, QuerySchemeConfiguration? Configuration);

/// <summary>默认选择只属于当前用户和上下文，与共享者的默认无关。</summary>
/// <param name="SchemeId">选择的可见方案；空值清除选择。</param>
/// <param name="Version">默认记录自己的版本，首次为零。</param>
public sealed record SetDefaultQuerySchemeRequest([property: JsonRequired] Guid? SchemeId, [property: JsonRequired] long Version);

/// <summary>默认选择及当前可执行状态。</summary>
/// <param name="Scheme">可见且结构有效的默认详情；不可用时为空。</param>
/// <param name="IsUnavailable">原选择已撤销、删除或结构失效。</param>
/// <param name="Version">个人默认记录的并发版本。</param>
public sealed record DefaultQuerySchemeResponse(QuerySchemeDetail? Scheme, bool IsUnavailable, long Version);
