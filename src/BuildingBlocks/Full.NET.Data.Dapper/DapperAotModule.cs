// Native AOT 发布时为 Dapper 启用源生成与 Guid/DateTimeOffset 类型处理器。
/// <summary>Dapper Native AOT 模块级属性；注册源生成与 Guid/DateTimeOffset 类型处理器。</summary>
#if FULLNET_AOT_COMPILE
global using Dapper;

[module: DapperAot]
[module: TypeHandler<Guid, Full.NET.Data.Dapper.AssignedGuidAotTypeHandler>]
[module: TypeHandler<DateTimeOffset, Full.NET.Data.Dapper.UtcDateTimeOffsetAotTypeHandler>]
#endif
