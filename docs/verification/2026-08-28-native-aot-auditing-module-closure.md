# Auditing 模块 Native AOT 闭环验证

## 结论

Auditing Host.Api 可达 SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物在 SQL Server/MySQL 上均完成 Operation、Exception、Outbound 审计写入读回，以及 Access 空页查询；不外推为 Worker Retention 原生进程闭环。

## 变更边界

- 查询、Dashboard 与 Retention 的匿名参数替换为固定键名字典。
- 动态微批 SQL 原本已返回显式字典参数，继续保留批次变形语义，不强行套固定 Command Plan。
- 注册 Access、Operation、Exception、Outbound 及两类 Dashboard 共六个严格投影物化器。
- 未改变可靠性分级、Channel/事务、脱敏、权限、数据库结构或公共 API。

## RED 与调试证据

实现前两项 Architecture 门禁 2/2 失败：六个文件仍含匿名 SQL 参数，且 contributor 不存在。

首次原生 E2E 错误假设普通 HTTP 请求会写入 `fn_auditing_access_log`，双库均在 Access 行等待超时。模块管道和原生日志证明这里只存在 Operation/Exception 写入 Middleware，普通 HTTP 可观测日志不是 Access 表生产者。测试随后改为执行 Access 空页查询；真实写入读回由 Operation、Exception、Outbound 三类承担。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Auditing Architecture RED | 2/2 按预期失败 |
| Auditing Architecture GREEN | 2/2，通过 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| Integration Release build | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,148,240 bytes |
| Linux Native SQL Server/MySQL | 2/2，通过；2 分 54 秒 |
| `pnpm test:inner -- --snapshot native-aot-auditing-20260828` | 4/4，通过 |
| `pnpm test:slice -- --snapshot native-aot-auditing-20260828` | 12/12 可执行项通过；5 项 Linux-only Native 测试在 Windows 为 Inconclusive，命令因此退出 1 |
| `pnpm test:governance` | 52/52，通过 |
| `pnpm test:naming` | 29/30；唯一失败来自既有 Migration 100 的 4 条 `FNSQL003`，不在本任务影响集 |
| 独立代码审查 | 无 Critical/Important；修正文档中 Dashboard 运行时覆盖的过度声明 |
| `git diff --check` | 通过 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-auditing-linux-local.trx`。

## 剩余边界

- Access 记录没有 Host.Api 普通请求生产者，本次只验证空页查询；非空 Access 行由既有双库 Integration 覆盖。
- Dashboard 物化器完成静态注册与 AOT 编译；本次原生进程没有请求工作台端点，不宣称运行时读取闭环。
- Retention 只完成参数静态闭包；它仅在 Worker 装配，本记录不宣称 Worker Native AOT。
