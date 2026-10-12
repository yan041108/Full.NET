using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Full.NET.UnitTests")]
// 仅向隔离数据库验收暴露协调器，不扩大生产模块间的实现依赖。
[assembly: InternalsVisibleTo("Full.NET.IntegrationTests")]
