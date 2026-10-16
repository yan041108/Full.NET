using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Full.NET.UnitTests")]
// 仅向隔离数据库验收暴露协调器，不扩大生产模块间的实现依赖。
[assembly: InternalsVisibleTo("Full.NET.IntegrationTests")]
// 内部边界的替身仅供测试使用，避免为代理生成器把实现类型扩大为公共契约。
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
