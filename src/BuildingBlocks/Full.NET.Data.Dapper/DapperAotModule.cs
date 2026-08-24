#if FULLNET_AOT_COMPILE
global using Dapper;

// Native AOT 发布闭包启用 Dapper 拦截器代码生成，避免运行时反射 emit。
[module: DapperAot]
#endif
