namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 表示生成器已经确认的数据访问作用域；未确认时禁止生成可执行的数据访问骨架。
/// </summary>
public enum FullNetCrudDataScope
{
    /// <summary>尚未确认数据作用域；生成器禁止为该状态产出可执行的数据访问骨架。</summary>
    Unspecified = 0,

    /// <summary>数据必须按当前租户过滤，对应 SqlTenantBinding.CurrentTenantId 的租户作用域语句。</summary>
    TenantRequired = 1,

    /// <summary>数据只在可信 Host 上下文内可读，对应 SqlTenantBinding.None 且不可在租户请求路径使用。</summary>
    HostOnly = 2,

    /// <summary>全局数据不按租户隔离，但仍需在 SQL 中以行条件精确限制作用域并进入 Global 语句目录。</summary>
    Global = 3,
}
