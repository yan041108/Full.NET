// AOT 构建下向模块 Dapper 物化程序集开放内部 SQL 参数注册，避免反射扫描。
/// <summary>Dapper AOT 友元程序集声明；向各业务模块暴露内部 SQL 参数注册。</summary>
#if FULLNET_AOT_COMPILE
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Identity")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Tenancy")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Organization")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.CodeGeneration")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Files")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Notifications")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Messaging")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Settings")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Jobs")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.SerialNumbers")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Document")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Auditing")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Full.NET.Modules.Workflow")]
#endif
