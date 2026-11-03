using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Full.NET.UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
// 集成测试直接验证受控读查询的 SQL 与租户边界，不将内部服务扩大为公共 API。
[assembly: InternalsVisibleTo("Full.NET.IntegrationTests")]
