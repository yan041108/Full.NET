namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 表示生成器已经确认的数据访问作用域；未确认时禁止生成可执行的数据访问骨架。
/// </summary>
public enum FullNetCrudDataScope
{
    Unspecified = 0,
    TenantRequired = 1,
    HostOnly = 2,
    Global = 3,
}
