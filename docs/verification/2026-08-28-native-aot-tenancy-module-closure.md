# Tenancy 模块 Native AOT 闭环验证

## 结论

Tenancy Host.Api 可达 SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物已在 SQL Server/MySQL 上创建真实租户套餐，通过重复编码查询执行 Identity 投影，并通过分页与详情执行完整套餐投影。

## 变更边界

- 六个 API 服务集合可达文件的匿名 SQL 参数替换为固定键名字典，保留 SQL、scope、事务、乐观并发及缓存失效语义。
- 补齐 `TenantPackageIdentityRecord` 六列、`TenantPackageRecord` 七列与 `LocalTenantSeedSummary` 六列 materializer；`COUNT` 投影保持 `Int64`。
- 原生 E2E 创建唯一套餐，重复创建必须返回 `tenancy.tenant_package.code_exists`，随后分页与按 ID 均须命中创建行，不以空结果冒充物化覆盖。
- `LocalTenantSeedContributor` 的实际执行仍由 Migrator 独占，但当前通过模块入口注册进 API 服务集合，因此参数和结果投影同样纳入静态闭包；本记录不声明 Migrator Native AOT。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Tenancy Architecture RED | 初始 2/2 按预期失败：5 个匿名参数文件、2 个缺失 materializer；审查扩展后再次 2/2 失败：Seed 匿名参数与缺失投影 |
| Tenancy Architecture GREEN | 2/2，通过 |
| Integration Release build | 串行复验 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 审查修复后通过；9 条允许的第三方警告；ELF 72,148,064 bytes |
| Linux Native SQL Server/MySQL | 审查修复后 5/5，通过；5 分 07 秒 |
| Native AOT/Dapper Architecture | 58/58，通过 |
| `pnpm test:inner -- --snapshot native-aot-tenancy-20260828` | 5/5，通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | 初审 1 个 Important 已修复；复审无 Critical/Important/Minor |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-tenancy-final-linux-local.trx`。

## Harness 调试记录

首次把 Integration Release build 与 AOT analyzer 并行运行时，两者争用共享 `bin/Release`，复制 `Full.NET.Messaging.Abstractions.pdb` 失败。分析器完成后未修改代码，串行重跑 Integration build 即通过；该失败归类为本地验证编排竞争，不是 Tenancy 产品缺陷。

最终 Docker publish 后，两个项目的 `project.assets.json` 仍指向容器包目录 `/root/.nuget/packages/`，导致首次 inner 在 Windows 上报 `NETSDK1064`。检查确认本机包完整，执行 `dotnet restore Full.NET.slnx --force-evaluate` 重写资产路径后，原命令通过。

## 剩余边界

- 本记录只声明 Host.Api 的 Tenancy API 可达闭包，不声明 Migrator seed 或 Worker 的原生进程运行验证。
