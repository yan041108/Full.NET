# SerialNumbers 模块 Native AOT 闭环验证

## 结论

SerialNumbers Host 规则 API 已完成 Native AOT 静态闭包，并由重新发布的 Linux `linux-x64` 原生 Host.Api 在 SQL Server 与 MySQL 上验证。覆盖规则创建、按 Id 读取、分页多结果读取和纯函数预览；未改变公共 API、数据库结构、权限、事务或流水号分配并发语义。

本切片不代表 Document、Auditing、Worker 或 Migrator 已完成 Native AOT 闭包，也不构成生产容量证明。

## 实现边界

- SerialNumbers 全部 SQL 调用改用固定键名参数工厂，静态门禁拒绝匿名 SQL 参数回归。
- 为 `SerialNumberRuleRecord`、`AllocatedCounterValue`、`SerialNumberAllocationRecord` 注册显式 ordinal 物化器。
- `DapperAotSqlExecution` 增加顺序多结果 ADO.NET 读取路径；AOT 下不再由 `QueryMultipleAsync` 进入 Dapper Reflection.Emit。
- 多结果执行沿用现有 AOT Command Rental、连接/事务、超时、取消和回收边界；当前 SerialNumbers 未登记专用静态 Command Plan，仍使用安全回退创建命令。
- SQL Server `tinyint` 与 MySQL 数值读取通过跨 Provider `Convert.ToInt32` 收敛。

## RED 与故障证据

初始 Architecture 过滤器 2/2 失败：检测到两个 SerialNumbers 服务仍传递匿名 SQL 参数，且缺少三个行物化器注册。

首轮 Linux 原生 E2E 暴露两个仅靠编译无法发现的故障：

- MySQL 分页触发 `PlatformNotSupportedException`，调用栈进入 Dapper `CreateParamInfoGenerator`，证明 `QueryMultipleAsync` 尚无 AOT 执行路径。
- SQL Server 单条读取触发 `InvalidCastException`，原因是 `tinyint` 被 `GetInt32` 强制读取。

修复后使用新发布 ELF 重跑，两种 Provider 均通过。

## 新鲜验证结果

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-restore --filter "FullyQualifiedName~SerialNumbersModule_"` | 2/2 通过 |
| `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~DapperAot"` | 7/7 通过 |
| `pnpm test:aot:analyzers` | 通过，0 warning / 0 error |
| `pnpm test:dotnet:architecture --selection api-native-aot` | 51/51 通过 |
| `pnpm test:aot:publish:linux` | 通过；9 个已登记第三方告警；ELF 72,132,112 B；356,041 ms |
| Linux 容器运行 `NativeApiSqlServerE2ETests` 与 `NativeApiMySqlE2ETests` | 2/2 通过，0 失败、0 跳过，2m50.455s |
| `pnpm test:inner -- --snapshot native-aot-serialnumbers-20260828` | 4/4 通过 |
| `pnpm test:slice -- --snapshot native-aot-serialnumbers-20260828` | smoke 8/8、非原生聚焦 2/2 通过；Windows 上 5 条 Native 用例按设计 Inconclusive，命令退出 1；对应双库核心 Native 用例已在 Linux 直接执行并通过 |
| `pnpm test:governance` | 52/52 通过 |
| `pnpm test:naming` | 29/30 通过；唯一失败来自基线迁移 `100_MessagingDomainAuditRequestedOutcome.sql` 的 4 条 `FNSQL003`，不在本任务影响集 |
| `git diff --check` | 无空白错误；仅 Git 行尾转换提示 |
| 独立代码审查 | 未发现 Critical/Important 问题 |

原生测试结果：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-serialnumbers-linux-local.trx`。

## 未验证边界

- 未验证 SerialNumbers 被其他业务模块调用时的完整分配 HTTP 链路；现有双库 SerialNumbers Integration 仍负责分配并发与幂等语义。
- 未验证 Worker/Migrator 原生产物、多实例部署或容量指标。
- Document 与 Auditing 仍应作为独立 Native AOT 模块切片处理，避免把不同持久化和运行边界混入本证据。
