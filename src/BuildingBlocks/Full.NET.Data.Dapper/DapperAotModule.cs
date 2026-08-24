#if FULLNET_AOT_COMPILE
global using Dapper;

[module: DapperAot]
[module: TypeHandler<Guid, Full.NET.Data.Dapper.AssignedGuidAotTypeHandler>]
[module: TypeHandler<DateTimeOffset, Full.NET.Data.Dapper.UtcDateTimeOffsetAotTypeHandler>]
#endif
